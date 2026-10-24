"""Judge a run's training checkpoints on the realism bands, before promoting any.

WHY THIS EXISTS
---------------
The realism gate (promote_brain.py --check-realism) only ever saw a brain after it
was promoted. football_base22 trained for 3.1M more steps, was promoted as
Football_v03, and failed all five bands; football_base24 at 5 hours read a
touchdown on 28% of plays and no sacks at all. A step count says nothing about
whether the game is football. This plays the four fixed-seed games with a
checkpoint's brains (Editor_CheckpointRealism) and records the verdict per
checkpoint, so a run's milestone is "inside the bands" and the brain promoted is
the checkpoint that plays football, not merely the last one written.

WHAT IT DOES
------------
For each Offense checkpoint picked (--steps, or the newest --last N), takes the
other three brains' checkpoints written NEAREST IN TIME to it (their intervals
differ: the quarterback and coordinator decide far less often), checks every
shape with promote_brain.py's own gate, stages the four .onnx files, and runs
Unity headless. Each verdict is appended to results/<run>/realism_checkpoints.json
and the table of all of them is printed, best first.

Unity is the Editor: CLAUDE.md (and the user's standing rule) is to close it during
a long training run, so this refuses to start while mlagents-learn is running unless
--allow-during-training is given. The usual use is the sweep after a run ends.

WITH THE EDITOR OPEN, PASS --in-editor. A second, batch-mode Unity cannot open a
project the Editor holds: it exits 1 within two seconds, and every checkpoint of a
sweep came back NOT EVALUATED that way (2026-10-24). --in-editor drives the open
Editor through the `unity` CLI instead (Editor_CheckpointRealism.RunInEditor) and
waits for each report to appear.

Usage
-----
    .venv/Scripts/python.exe Tools/checkpoint_realism.py --run football_base24 --last 4
    .venv/Scripts/python.exe Tools/checkpoint_realism.py --run football_base24 --steps 3099990 4099985
    .venv/Scripts/python.exe Tools/checkpoint_realism.py --run football_base25 --in-editor --last 3
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
RESULTS_DIR = REPO_ROOT / "results"
STAGE_ROOT = RESULTS_DIR / "audit" / "checkpoint_stage"
REALISM_DIR = RESULTS_DIR / "realism"
DEFAULT_UNITY = Path(r"C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe")
GROUPS = ("Offense", "Defense", "Quarterback", "DefenseCall")
LEAD = "Offense"

sys.path.insert(0, str(Path(__file__).resolve().parent))
import promote_brain  # noqa: E402  (the shape gate and the bands, not copies of them)


def checkpoints(run_dir: Path, behavior: str) -> dict:
    """{step: path} for every <behavior>-<step>.onnx the run kept."""
    found = {}
    for path in (run_dir / behavior).glob(f"{behavior}-*.onnx"):
        match = re.search(r"-(\d+)\.onnx$", path.name)
        if match:
            found[int(match.group(1))] = path
    return dict(sorted(found.items()))


def nearest_in_time(candidates: dict, moment: float) -> Path:
    return min(candidates.values(), key=lambda path: abs(path.stat().st_mtime - moment))


def trainer_running() -> bool:
    try:
        listing = subprocess.check_output(["tasklist"], text=True, errors="replace")
    except (OSError, subprocess.CalledProcessError):
        return False
    return "mlagents-learn" in listing


def judge(report: dict) -> dict:
    """Means and per-band verdicts, the way promote_brain.check_realism computes them."""
    games = report.get("games", [])
    for game in games:
        plays = game.get("scrimmage_plays") or 0
        first = game.get("first_downs")
        game["first_downs_per_play"] = first / plays if first is not None and plays else None

    means, inside = {}, {}
    for key, (low, high) in promote_brain.REALISM_BANDS.items():
        values = [game.get(key) for game in games]
        if not values or any(value is None for value in values):
            continue
        means[key] = sum(values) / len(values)
        inside[key] = low <= means[key] <= high
    return {"games": len(games), "means": means, "inside": inside,
            "passed": bool(inside) and all(inside.values())
                      and len(games) >= promote_brain.REALISM_MIN_GAMES}


def distance_from_bands(means: dict) -> float:
    """How far outside the bands, in band-widths, summed: 0 for a pass. Ranks failures."""
    total = 0.0
    for key, (low, high) in promote_brain.REALISM_BANDS.items():
        if key in means:
            value = means[key]
            total += max(0.0, low - value, value - high) / (high - low)
    return total


def evaluate(unity: Path, stage: Path, label: str, log: Path) -> dict | None:
    before = set(REALISM_DIR.glob("realism-*.json")) if REALISM_DIR.is_dir() else set()
    command = [str(unity), "-batchmode", "-projectPath", str(REPO_ROOT),
               "-executeMethod", "PoFootball.EditorTools.Editor_CheckpointRealism.RunBatch",
               "-checkpointDir", str(stage), "-checkpointLabel", label, "-logFile", str(log)]
    started = time.time()
    code = subprocess.call(command)
    after = set(REALISM_DIR.glob("realism-*.json")) if REALISM_DIR.is_dir() else set()
    written = sorted(after - before, key=lambda p: p.stat().st_mtime)
    print(f"  Unity exited {code} after {time.time() - started:.0f} s; log {log}")
    if not written:
        return None
    report = json.loads(written[-1].read_text(encoding="utf-8"))
    report["_path"] = written[-1].relative_to(REPO_ROOT).as_posix()
    return report


IN_EDITOR_TIMEOUT_SECONDS = 45 * 60
IN_EDITOR_POLL_SECONDS = 15


def unity_cli(*arguments: str, timeout: int = 120) -> str:
    """One `unity` CLI call; its last line, which carries the JSON result."""
    try:
        output = subprocess.run(["unity", *arguments], capture_output=True, text=True,
                                timeout=timeout, stdin=subprocess.DEVNULL, errors="replace").stdout
    except (OSError, subprocess.TimeoutExpired) as error:
        return f"unity CLI failed: {error}"
    lines = [line for line in output.splitlines() if line.strip()]
    return lines[-1] if lines else ""


def evaluate_in_editor(stage: Path, label: str) -> dict | None:
    """The four games in the Editor that is already open; the report it writes, or None."""
    before = set(REALISM_DIR.glob("realism-*.json")) if REALISM_DIR.is_dir() else set()
    # By reflection: the class is internal to Assembly-CSharp-Editor and eval
    # compiles its snippet into an assembly of its own.
    code = ("return (string)System.Type.GetType(\"PoFootball.EditorTools.Editor_CheckpointRealism, "
            "Assembly-CSharp-Editor\").GetMethod(\"RunInEditor\").Invoke(null, new object[] { "
            f"@\"{stage}\", \"{label}\" }});")
    reply = unity_cli("command", "eval", "--code", code)
    match = re.search(r'"result":"([^"]*)"', reply)
    if not match or match.group(1) != "started":
        print(f"  NOT STARTED: {match.group(1) if match else reply[:200]}")
        return None

    started = time.time()
    was_playing = False
    stopped_polls = 0
    while time.time() - started < IN_EDITOR_TIMEOUT_SECONDS:
        time.sleep(IN_EDITOR_POLL_SECONDS)
        after = set(REALISM_DIR.glob("realism-*.json")) if REALISM_DIR.is_dir() else set()
        written = sorted(after - before, key=lambda p: p.stat().st_mtime)
        if written:
            print(f"  four games in {time.time() - started:.0f} s (open Editor)")
            report = json.loads(written[-1].read_text(encoding="utf-8"))
            report["_path"] = written[-1].relative_to(REPO_ROOT).as_posix()
            return report
        playing = '"playMode":"playing"' in unity_cli("command", "editor_status", timeout=30)
        was_playing = was_playing or playing
        stopped_polls = 0 if playing else stopped_polls + 1
        if was_playing and stopped_polls >= 4:
            print("  ABANDONED: the Editor left play mode with no report (see the Console)")
            return None
    print(f"  TIMED OUT after {IN_EDITOR_TIMEOUT_SECONDS} s with no report")
    return None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--run", required=True)
    parser.add_argument("--steps", type=int, nargs="*", help=f"{LEAD} checkpoint steps to judge")
    parser.add_argument("--last", type=int, default=3, help=f"else the newest N {LEAD} checkpoints")
    parser.add_argument("--unity", type=Path, default=DEFAULT_UNITY)
    parser.add_argument("--allow-during-training", action="store_true")
    parser.add_argument("--in-editor", action="store_true",
                        help="drive the open Editor through the unity CLI, not a batch-mode Unity")
    args = parser.parse_args()

    run_dir = RESULTS_DIR / args.run
    if not run_dir.is_dir():
        print(f"No run at results/{args.run}", file=sys.stderr)
        return 2
    if not args.in_editor and not args.unity.is_file():
        print(f"No Unity at {args.unity}; pass --unity", file=sys.stderr)
        return 2
    if trainer_running() and not args.allow_during_training:
        print("mlagents-learn is running. The Editor is closed during long runs; judge the "
              "checkpoints after it ends, or pass --allow-during-training.", file=sys.stderr)
        return 2

    available = {group: checkpoints(run_dir, group) for group in GROUPS}
    missing = [group for group, found in available.items() if not found]
    if missing:
        print(f"No checkpoints for {', '.join(missing)} in {run_dir}", file=sys.stderr)
        return 2

    lead = available[LEAD]
    steps = args.steps or list(lead)[-args.last:]
    contract = promote_brain.expected_contract()
    ledger_path = run_dir / "realism_checkpoints.json"
    ledger = json.loads(ledger_path.read_text(encoding="utf-8")) if ledger_path.exists() else []

    for step in steps:
        if step not in lead:
            print(f"{LEAD}-{step}.onnx not in {run_dir / LEAD}; have {list(lead)}", file=sys.stderr)
            return 2
        moment = lead[step].stat().st_mtime
        chosen = {group: (lead[step] if group == LEAD else nearest_in_time(available[group], moment))
                  for group in GROUPS}

        label = f"{args.run}@{step}"
        print(f"\n{label}: " + ", ".join(path.name for path in chosen.values()))

        problems = [f"{group}: {problem}" for group, path in chosen.items()
                    for problem in promote_brain.validate(group, path, contract)]
        if problems:
            print("  REFUSED by the shape gate:\n    " + "\n    ".join(problems))
            ledger.append({"label": label, "refused": problems})
            continue

        stage = STAGE_ROOT / label.replace("@", "_")
        stage.mkdir(parents=True, exist_ok=True)
        for group, path in chosen.items():
            shutil.copyfile(path, stage / f"{group}.onnx")

        report = (evaluate_in_editor(stage, label) if args.in_editor
                  else evaluate(args.unity, stage, label, stage / "unity.log"))
        if report is None or not report.get("brains_loaded", False):
            print("  NOT EVALUATED: no report, or the checkpoint table was refused "
                  "(another contract revision?). See the Unity log.")
            ledger.append({"label": label, "evaluated": False})
            continue

        verdict = judge(report)
        verdict.update({"label": label, "report": report["_path"],
                        "files": {group: path.name for group, path in chosen.items()}})
        ledger.append(verdict)
        print("  " + ("PASSED" if verdict["passed"] else "FAILED") + "  " + ", ".join(
            f"{key} {value:.2f}{'' if verdict['inside'][key] else ' OUT'}"
            for key, value in verdict["means"].items()))

    ledger_path.write_text(json.dumps(ledger, indent=2), encoding="utf-8")

    judged = [entry for entry in ledger if entry.get("means")]
    judged.sort(key=lambda entry: (not entry["passed"], distance_from_bands(entry["means"])))
    print(f"\nAll judged checkpoints of {args.run}, best first ({ledger_path.name}):")
    for entry in judged:
        print(f"  {'PASS' if entry['passed'] else 'fail'} {entry['label']:32} "
              f"{distance_from_bands(entry['means']):5.2f} band-widths out")

    return 0 if judged and judged[0]["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
