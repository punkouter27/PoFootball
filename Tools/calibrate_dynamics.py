"""Measure every role's sprint against real football, from the C# constants.

WHY THIS EXISTS
---------------
The body dynamics were tuned twice by argument (revision 3 lowered
LINEAR_DAMPING 1.5 -> 0.8 because 1.5 "read as fast-forward"; revision 14 raised
the lines to 2.4 so blocks would hold) and never once against a stopwatch. Under
linear drag the whole sprint is two numbers per role — top speed V and the time
constant 1/d — so it can be put next to the NFL combine's 10- and 40-yard times
without running Unity at all.

The integrator is Box2D's as Unity runs it (v += h*F/m; v /= 1 + h*d; x += h*v),
and Systems_PhysicsContractTests.SprintSplit_MatchesTheIntegratorThisToolModels
pins that the real Physics2D agrees with it tick for tick, so these numbers are
the game's and not a model of it.

WHAT IT REPORTS
---------------
Per role: time to 90% of top speed, the 10- and 40-yard times from a standing
start, and the distance to stop from top speed coasting (damping alone: what
revision 16 does when a policy lets off) and braking at full reverse drive (what
the revision 17 velocity controller does). Then, per role, the damping that would
best fit both splits at the same top speed — a PROPOSAL, because CLAUDE.md is
explicit that a balance change is judged on the 4-seed realism line and nothing
else, and damping also sets how hard a body pushes (DriveForceOf = V * m * d).

The references are approximate combine averages by position (hand-timed era and
electronic mixed, rounded); they are a yardstick for "a second slow" or not, not a
target to two decimals. Combine runs are from a three-point stance with no pads
and no ball, so a game body should be a little SLOWER than these, not faster.

Usage
-----
    .venv/Scripts/python.exe Tools/calibrate_dynamics.py
    .venv/Scripts/python.exe Tools/calibrate_dynamics.py --json results/audit/calibration.json
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SIM_CONSTANTS = REPO_ROOT / "Assets/Scripts/Systems/Model/Systems_SimConstants.cs"
ROLE_TABLE = REPO_ROOT / "Assets/Scripts/Systems/Model/Systems_RoleTable.cs"

TICK = 0.02
YARD = 0.9144

# Approximate NFL combine averages, (10-yard split, 40-yard dash) in seconds.
REFERENCE = {
    "WideReceiver": (1.55, 4.48),
    "Cornerback": (1.54, 4.48),
    "Safety": (1.57, 4.55),
    "RunningBack": (1.57, 4.53),
    "Linebacker": (1.62, 4.70),
    "TightEnd": (1.64, 4.73),
    "Fullback": (1.68, 4.85),
    "Quarterback": (1.66, 4.80),
    "OffensiveLine": (1.83, 5.27),
    "DefensiveLine": (1.72, 4.95),
}

# A split this much slower than the reference is "far off" and is reported so.
TOLERANCE = 0.10


def read_float_const(source: Path, name: str) -> float:
    match = re.search(rf"const float {name}\s*=\s*([0-9.]+)f", source.read_text(encoding="utf-8"))
    if not match:
        raise SystemExit(f"{name} not found in {source.name}")
    return float(match.group(1))


def read_role_switch(function: str) -> dict:
    """{role: value} from one `public static float <function>(Systems_PlayerRole role)`
    switch in Systems_RoleTable, reading `case`, `default` and `return Nf;` lines."""
    text = ROLE_TABLE.read_text(encoding="utf-8")
    start = text.find(f"public static float {function}(Systems_PlayerRole role)")
    if start < 0:
        raise SystemExit(f"{function} not found in {ROLE_TABLE.name}")
    body = text[start:text.find("\n        }\n", start)]

    values, pending, default = {}, [], None
    for line in body.splitlines():
        line = line.strip()
        case = re.match(r"case Systems_PlayerRole\.(\w+):", line)
        if case:
            pending.append(case.group(1))
        elif line.startswith("default:"):
            pending.append("*")
        else:
            ret = re.match(r"return ([0-9.]+)f;", line)
            if ret:
                for role in pending:
                    if role == "*":
                        default = float(ret.group(1))
                    else:
                        values[role] = float(ret.group(1))
                pending = []

    for role in REFERENCE:
        values.setdefault(role, default)
    return values


def sprint(top_speed: float, damping: float, brake: bool = False) -> dict:
    """One standing-start sprint, then one stop from top speed, at full drive."""
    accel = top_speed * damping  # DriveForceOf / mass
    v = x = t = 0.0
    t90 = t10 = t40 = None
    while t40 is None and t < 30.0:
        v = (v + TICK * accel) / (1.0 + TICK * damping)
        x += v * TICK
        t += TICK
        if t90 is None and v >= 0.9 * top_speed:
            t90 = t
        if t10 is None and x >= 10 * YARD:
            t10 = t
        if x >= 40 * YARD:
            t40 = t

    v, stop = top_speed, 0.0
    while v > 0.3:
        if brake:
            v = max(0.0, v - TICK * accel)
        v /= 1.0 + TICK * damping
        stop += v * TICK

    return {"t90": t90, "t10": t10, "t40": t40, "stop": stop}


def best_damping(top_speed: float, ref10: float, ref40: float) -> float:
    """The damping whose 10- and 40-yard times sit closest to the reference, in a
    least-squares sense, at an unchanged top speed."""
    best, best_error = None, float("inf")
    for step in range(40, 401):
        damping = step / 100.0
        run = sprint(top_speed, damping)
        if run["t40"] is None:
            continue
        error = ((run["t10"] - ref10) / ref10) ** 2 + ((run["t40"] - ref40) / ref40) ** 2
        if error < best_error:
            best, best_error = damping, error
    return best


def joint_fit(ref10: float, ref40: float) -> tuple[float, float]:
    """The (top speed, damping) pair that runs both reference splits — two numbers
    for two targets, so an exact solve to the grid's resolution."""
    best, best_error = (None, None), float("inf")
    for speed_step in range(60, 121):
        top = speed_step / 10.0
        for damping_step in range(40, 401, 2):
            damping = damping_step / 100.0
            run = sprint(top, damping)
            if run["t40"] is None:
                continue
            error = ((run["t10"] - ref10) / ref10) ** 2 + ((run["t40"] - ref40) / ref40) ** 2
            if error < best_error:
                best, best_error = (top, damping), error
    return best


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--json", type=Path, help="also write the table here")
    args = parser.parse_args()

    linear = read_float_const(SIM_CONSTANTS, "LINEAR_DAMPING")
    line = read_float_const(SIM_CONSTANTS, "LINE_LINEAR_DAMPING")
    speeds = read_role_switch("TopSpeedOf")

    rows = []
    print(f"LINEAR_DAMPING {linear} (tau {1 / linear:.2f} s), LINE_LINEAR_DAMPING {line} "
          f"(tau {1 / line:.2f} s), tick {TICK}s\n")
    print(f"{'role':14} {'V m/s':>6} {'t90':>5} {'10yd':>12} {'40yd':>12} "
          f"{'coast':>6} {'brake':>6}  {'fit d':>5}  {'fit V,d':>9}")

    far_off = 0
    for role, (ref10, ref40) in REFERENCE.items():
        damping = line if role in ("OffensiveLine", "DefensiveLine") else linear
        top = speeds[role]
        run = sprint(top, damping)
        braked = sprint(top, damping, brake=True)
        fit = best_damping(top, ref10, ref40)
        fit_top, fit_damping = joint_fit(ref10, ref40)
        # Below this no damping runs the forty in time: the average speed over it
        # is already faster than the body's ceiling.
        too_slow = top < (40 * YARD) / ref40
        slow10 = (run["t10"] - ref10) / ref10
        slow40 = (run["t40"] - ref40) / ref40
        flag = " FAR OFF" if max(slow10, slow40) > TOLERANCE else ""
        if too_slow:
            flag += ", V below the forty's average"
        far_off += bool(flag)
        print(f"{role:14} {top:6.1f} {run['t90']:5.2f} "
              f"{run['t10']:5.2f} ({slow10:+4.0%}) {run['t40']:5.2f} ({slow40:+4.0%}) "
              f"{run['stop']:5.1f}m {braked['stop']:5.1f}m  {fit:5.2f}  {fit_top:4.1f},{fit_damping:4.2f}{flag}")
        rows.append({
            "role": role, "top_speed": top, "damping": damping,
            "t90": run["t90"], "t10": run["t10"], "t40": run["t40"],
            "ref10": ref10, "ref40": ref40, "coast_stop_m": run["stop"],
            "brake_stop_m": braked["stop"], "best_fit_damping": fit,
            "joint_fit_top_speed": fit_top, "joint_fit_damping": fit_damping,
            "top_speed_below_forty_average": too_slow,
        })

    print(f"\n{far_off} of {len(REFERENCE)} roles more than {TOLERANCE:.0%} slower than the "
          f"reference over 10 or 40 yards.")
    print("A damping change is a dynamics change: contract revision, every brain refused,"
          " and it must be judged on the 4-seed realism line before it is kept.")

    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps({
            "linear_damping": linear, "line_linear_damping": line, "roles": rows,
        }, indent=2), encoding="utf-8")
        print(f"wrote {args.json}")

    return 1 if far_off else 0


if __name__ == "__main__":
    sys.exit(main())
