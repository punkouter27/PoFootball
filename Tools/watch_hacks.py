"""Watch a live run for the reward hacks this project has already paid for once.

WHY THIS EXISTS
---------------
Every one of these was found by replaying checkpoints AFTER the run, hours or
millions of steps late. Each has a signature in the series Agent_Telemetry and
Agent_FootballPlayer already write, and each signature was visible in
TensorBoard long before anybody looked:

    Play/TimeExpiredRate   the stall: football_base20's defense shadowed carriers
                           past the sticks and never tackled (6.8% of plays)
    Play/FlagRate          football_base18/19's stalk blocks on backpedalling
                           corners, flagged on 12% of plays
    Control/SpeedClampRate a policy exploiting solver blow-ups; promote_brain.py
                           refuses a run above 0.001 anyway
    Play/SackRate          football_base22's passive defense: zero sacks in three
                           million steps
    Play/TouchdownRate     the same passive defense from the other end
    Call/Entropy           football_base05's play call collapsing onto one option
    DefenseCall/<front>    football_base17's zero blitz on 100% of plays
    Reward/<brain>/ShapingShare
                           a shaping term outgrowing the result it shapes (only
                           once the env carries the reward ledger, base25 on)

WHAT IT DOES
------------
Polls the run's TensorBoard event files. A HALT rule that trips on N consecutive
summaries ends the watch with exit code 1 (and, with --stop-trainer, sends the
trainer CTRL+C so it exports its brains first). A WARN rule only prints. Exits 0
when --target steps are reached with nothing halted. --once prints one report
and exits, for a status line.

Every environment stat reaches every behavior's event file (ml-agents attributes
the stats side channel to all of them), so each rule names the brain whose STEP
COUNT it should be read against, not the brain that wrote it.

Usage
-----
    .venv/Scripts/python.exe Tools/watch_hacks.py --run football_base24 --once
    .venv/Scripts/python.exe Tools/watch_hacks.py --run football_base25 \
        --target 8000000 --stop-trainer --trainer-pid 21420

Reads event files only, like watch_entropy.py, and is safe against a live run.
"""

from __future__ import annotations

import argparse
import ctypes
import sys
import time
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
RESULTS_DIR = REPO_ROOT / "results"

HALT = "HALT"
WARN = "WARN"


@dataclass(frozen=True)
class Rule:
    name: str
    behavior: str
    tag: str
    above: bool
    threshold: float
    severity: str
    after_step: int
    why: str


# Thresholds are deliberately past what an honest run reads, not at it: the
# scripted game flags ~1.6% of plays, a trained one ~1-2%; ceiling plays were
# ~1% before the breakaway rule and 6-7% at the stall.
RULES = (
    Rule("stall", "Defense", "Play/TimeExpiredRate", True, 0.05, HALT, 0,
         "plays reaching the 600-tick ceiling: the base20 stall"),
    Rule("flags", "Offense", "Play/FlagRate", True, 0.06, HALT, 0,
         "flags on more than 6% of plays: the base18/19 block in the back"),
    Rule("solver", "Offense", "Control/SpeedClampRate", True, 0.001, HALT, 0,
         "pileup guard firing: a policy farming solver blow-ups"),
    Rule("no-sacks", "Defense", "Play/SackRate", False, 0.0005, WARN, 1_500_000,
         "no sacks at all: the base22 passive pass rush"),
    Rule("touchdowns", "Defense", "Play/TouchdownRate", True, 0.25, WARN, 1_000_000,
         "a touchdown on more than a quarter of plays: nobody is tackling"),
    Rule("call-collapse", "Quarterback", "Call/Entropy", False, 0.80, HALT, 0,
         "the play call has collapsed onto one option (watch_entropy.py)"),
)

# Prefixes whose largest member is judged as a share: one front on > 90% of plays.
FRONT_PREFIX = "DefenseCall/"
FRONT_BEHAVIOR = "DefenseCall"
FRONT_CEILING = 0.90

LEDGER_PREFIX = "Reward/"
LEDGER_SUFFIX = "/ShapingShare"
LEDGER_CEILING = 0.50


def load(behavior_dir: Path):
    """An EventAccumulator for one behavior, or None if it cannot be read yet."""
    try:
        from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
    except ImportError:
        print("tensorboard is not installed in this environment.", file=sys.stderr)
        raise SystemExit(2)

    if not behavior_dir.is_dir():
        return None

    try:
        accumulator = EventAccumulator(str(behavior_dir), size_guidance={"scalars": 0})
        accumulator.Reload()
        return accumulator
    except Exception:
        # A half-written event file mid-flush is normal against a live run.
        return None


def tail(accumulator, tag: str, count: int) -> list:
    if accumulator is None or tag not in accumulator.Tags().get("scalars", []):
        return []
    return accumulator.Scalars(tag)[-count:]


def judge(rule: Rule, points: list, consecutive: int) -> tuple[bool, str]:
    """(tripped, one-line reading) for one rule."""
    if not points:
        return False, f"{rule.tag}: no data yet"

    latest = points[-1]
    values = [point.value for point in points]
    reading = f"{rule.tag} {latest.value:.4f} at {latest.step:,}"

    if latest.step < rule.after_step or len(points) < consecutive:
        return False, reading + " (not judged yet)"

    beyond = [(v > rule.threshold) if rule.above else (v < rule.threshold) for v in values]
    return all(beyond), reading


def check(run_dir: Path, consecutive: int) -> tuple[list, list, int]:
    """(halts, warnings, newest step) across every rule."""
    halts, warnings = [], []
    accumulators = {}
    newest = 0

    def get(behavior: str):
        if behavior not in accumulators:
            accumulators[behavior] = load(run_dir / behavior)
        return accumulators[behavior]

    for rule in RULES:
        points = tail(get(rule.behavior), rule.tag, consecutive)
        tripped, reading = judge(rule, points, consecutive)
        if points:
            newest = max(newest, points[-1].step)
        line = f"{rule.severity} {rule.name}: {reading} - {rule.why}"
        if tripped:
            (halts if rule.severity == HALT else warnings).append(line)
        else:
            print(f"  ok {rule.name}: {reading}")

    fronts = get(FRONT_BEHAVIOR)
    if fronts is not None:
        tags = [t for t in fronts.Tags().get("scalars", []) if t.startswith(FRONT_PREFIX)]
        shares = {t: tail(fronts, t, consecutive) for t in tags}
        shares = {t: p for t, p in shares.items() if len(p) == consecutive}
        if shares:
            top = max(shares, key=lambda t: shares[t][-1].value)
            if all(point.value > FRONT_CEILING for point in shares[top]):
                halts.append(
                    f"HALT front-collapse: {top} {shares[top][-1].value:.2f} of plays "
                    f"- the base17 zero blitz")
            else:
                print(f"  ok front-collapse: top {top} {shares[top][-1].value:.2f}")

    for behavior in ("Offense", "Defense", "Quarterback", "DefenseCall"):
        accumulator = get(behavior)
        if accumulator is None:
            continue
        tag = f"{LEDGER_PREFIX}{behavior}{LEDGER_SUFFIX}"
        points = tail(accumulator, tag, consecutive)
        if len(points) == consecutive and all(p.value > LEDGER_CEILING for p in points):
            warnings.append(
                f"WARN shaping: {tag} {points[-1].value:.2f} - dense shaping is more "
                f"than half of {behavior}'s reward; the result no longer decides it")

    return halts, warnings, newest


def stop_trainer(pid: int) -> None:
    """CTRL+C, so mlagents-learn exports its .onnx files (results/ctrlc.py)."""
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.FreeConsole()
    if not kernel32.AttachConsole(pid):
        print(f"AttachConsole({pid}) failed: {ctypes.get_last_error()}", file=sys.stderr)
        return
    kernel32.SetConsoleCtrlHandler(None, True)
    kernel32.GenerateConsoleCtrlEvent(0, 0)
    time.sleep(1)
    kernel32.FreeConsole()
    print(f"sent CTRL+C to trainer {pid}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--run", required=True, help="run id under results/")
    parser.add_argument("--target", type=int, default=8_000_000,
                        help="stop watching once any rule's brain reaches this step")
    parser.add_argument("--consecutive", type=int, default=3,
                        help="summaries beyond a threshold before a rule trips")
    parser.add_argument("--interval", type=int, default=300, help="seconds between polls")
    parser.add_argument("--once", action="store_true", help="one report, then exit")
    parser.add_argument("--stop-trainer", action="store_true",
                        help="on a HALT, send the trainer CTRL+C (needs --trainer-pid)")
    parser.add_argument("--trainer-pid", type=int, default=0)
    args = parser.parse_args()

    run_dir = RESULTS_DIR / args.run
    if not run_dir.is_dir():
        print(f"No run at results/{args.run}", file=sys.stderr)
        return 2
    if args.stop_trainer and not args.trainer_pid:
        print("--stop-trainer needs --trainer-pid", file=sys.stderr)
        return 2

    while True:
        print(f"{time.strftime('%Y-%m-%dT%H:%M:%S')} {args.run}")
        halts, warnings, newest = check(run_dir, args.consecutive)

        for line in warnings + halts:
            print("  " + line)

        if halts:
            if args.stop_trainer:
                stop_trainer(args.trainer_pid)
            return 1

        if args.once:
            return 0

        if newest >= args.target:
            print(f"PASSED target {args.target:,} with nothing halted")
            return 0

        time.sleep(args.interval)


if __name__ == "__main__":
    sys.exit(main())
