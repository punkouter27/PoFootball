"""Audit what the brains observe: clamped floats, dead floats, duplicated floats.

WHY THIS EXISTS
---------------
Every observation is clamped to [-1, 1] and the trainer does no normalization
(Sensor_FootballState; normalize: false), so the only scaling a float ever gets is
the constant it is divided by in C#. Two failures follow and neither throws:

    a float pinned at its clamp most of the time carries no information there —
    relative distances are cut off at OBSERVATION_RANGE (40 m), so "the deep
    safety is 41 m away" and "70 m away" are the same input;

    two floats that move together are one float spending two inputs. Revision 12
    found the two sideline distances were NormalizeX three times over.

WHAT IT DOES
------------
Reads the ML-Agents .demo files Editor_ObservationAudit records (Tools >
PoFootball > Audit > Record Observations), per behavior and per sensor, and
reports for every float of the hand-built vector: range, mean, standard deviation,
the share of samples at the clamp, and whether it is binary or constant; then every
pair of continuous floats correlated above --corr. The rays are summarised per
sensor rather than float by float.

The recording is of the HEURISTIC, which is what runs without a trainer. Clamping
and constancy are properties of the field geometry and mostly carry over to a
policy; a correlation can be a habit of the script, and is labelled as one to
check, not a defect.

Usage
-----
    .venv/Scripts/python.exe Tools/audit_observations.py --demos results/audit/demos/<stamp>
    .venv/Scripts/python.exe Tools/audit_observations.py --demos <dir> --json results/audit/obs.json
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SENSOR = REPO_ROOT / "Assets/Scripts/Sensor/Sensor_FootballState.cs"

ROLES = ("Quarterback", "RunningBack", "WideReceiver", "TightEnd", "OffensiveLine",
         "DefensiveLine", "Linebacker", "Cornerback", "Safety", "Fullback")
CALLS = ("KeepQuarterback", "HandoffFullback", "HandoffHalfback", "Pass", "Punt",
         "FieldGoal", "PassDeep")

# The order Sensor_FootballState.Write fills the buffer in. Checked against
# OBSERVATION_SIZE below, so a new float that is not named here stops the audit
# rather than shifting every name after it by one.
VECTOR_NAMES = (
    [f"role/{r}" for r in ROLES]
    + ["pos/x", "pos/y", "vel/sideways", "vel/forward", "facing/cos", "facing/sin",
       "fatigue", "is_carrier", "ball/offset_a", "ball/offset_b", "ball/vel_a", "ball/vel_b",
       "ball/held", "ball/in_flight", "ball/kicked", "ball/loose",
       "field/to_goal", "field/to_line", "spin", "play_clock", "down", "distance"]
    + [f"call/{c}" for c in CALLS]
    + ["possession", "ball/height", "kind/kickoff", "kind/punt", "kind/placekick",
       "kind/two_point", "pre_snap", "trait/speed", "trait/strength", "trait/agility",
       "trait/discipline"]
)

DEFENSE_CALL_NAMES = (
    ["down", "distance", "to_goal", "two_point"] + [f"formation/{i}" for i in range(8)]
)

CLAMP = 0.999
CONSTANT_STD = 1e-6
SATURATION_FLAG = 0.20


def read_observation_size() -> int:
    match = re.search(r"const int OBSERVATION_SIZE = (\d+);", SENSOR.read_text(encoding="utf-8"))
    if not match:
        raise SystemExit("OBSERVATION_SIZE not found in Sensor_FootballState.cs")
    return int(match.group(1))


def load(demo_dir: Path) -> dict:
    """{behavior: {sensor index: (name, ndarray [samples, floats])}}."""
    import numpy as np
    from mlagents.trainers.demo_loader import load_demonstration

    rows = defaultdict(lambda: defaultdict(list))
    names = {}
    for path in sorted(demo_dir.glob("*.demo")):
        behavior = re.sub(r"_\d+$", "", path.stem)
        _, pairs, _ = load_demonstration(str(path))
        for pair in pairs:
            for index, observation in enumerate(pair.agent_info.observations):
                rows[behavior][index].append(list(observation.float_data.data))
                names[(behavior, index)] = observation.name or f"sensor{index}"

    result = {}
    for behavior, sensors in rows.items():
        result[behavior] = {
            index: (names[(behavior, index)], np.asarray(samples, dtype=np.float32))
            for index, samples in sensors.items() if samples and samples[0]
        }
    return result


def describe(values, labels: list[str]) -> list[dict]:
    import numpy as np

    stats = []
    for column in range(values.shape[1]):
        data = values[:, column]
        unique = np.unique(np.round(data, 4))
        binary = set(unique.tolist()) <= {0.0, 1.0}
        std = float(data.std())
        stats.append({
            "index": column,
            "name": labels[column] if column < len(labels) else f"[{column}]",
            "min": float(data.min()), "max": float(data.max()),
            "mean": float(data.mean()), "std": std,
            "at_clamp": float((np.abs(data) >= CLAMP).mean()),
            "binary": bool(binary),
            "constant": std < CONSTANT_STD,
        })
    return stats


def correlated_pairs(values, stats: list[dict], threshold: float) -> list[dict]:
    import numpy as np

    live = [s["index"] for s in stats if not s["binary"] and not s["constant"]]
    if len(live) < 2:
        return []
    matrix = np.corrcoef(values[:, live], rowvar=False)
    pairs = []
    for a in range(len(live)):
        for b in range(a + 1, len(live)):
            r = matrix[a, b]
            if np.isfinite(r) and abs(r) >= threshold:
                pairs.append({"a": stats[live[a]]["name"], "b": stats[live[b]]["name"], "r": float(r)})
    return sorted(pairs, key=lambda p: -abs(p["r"]))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--demos", type=Path, required=True, help="a recording directory")
    parser.add_argument("--corr", type=float, default=0.98, help="|r| at which a pair is reported")
    parser.add_argument("--json", type=Path, help="also write the full audit here")
    args = parser.parse_args()

    size = read_observation_size()
    if len(VECTOR_NAMES) != size:
        print(f"VECTOR_NAMES has {len(VECTOR_NAMES)} names; Sensor_FootballState writes {size}. "
              "Name the new floats before auditing.", file=sys.stderr)
        return 2

    if not args.demos.is_dir() or not any(args.demos.glob("*.demo")):
        print(f"No .demo files in {args.demos}", file=sys.stderr)
        return 2

    data = load(args.demos)
    report, findings = {}, []

    for behavior in sorted(data):
        report[behavior] = {}
        for index, (name, values) in sorted(data[behavior].items()):
            is_vector = values.shape[1] in (size, len(DEFENSE_CALL_NAMES))
            labels = list(VECTOR_NAMES) if values.shape[1] == size else list(DEFENSE_CALL_NAMES)
            stats = describe(values, labels if is_vector else [])
            pairs = correlated_pairs(values, stats, args.corr) if is_vector else []
            report[behavior][name] = {"samples": int(values.shape[0]), "floats": stats, "pairs": pairs}

            print(f"\n== {behavior} / {name}: {values.shape[0]:,} samples x {values.shape[1]} floats")
            if not is_vector:
                saturated = sum(s["at_clamp"] > 0.5 for s in stats)
                dead = sum(s["constant"] for s in stats)
                mean_clamp = sum(s["at_clamp"] for s in stats) / len(stats)
                print(f"   rays: {dead} constant floats, {saturated} at the clamp on most "
                      f"samples, {mean_clamp:.0%} of all ray floats at the clamp")
                continue

            for s in stats:
                notes = []
                if s["constant"]:
                    notes.append("CONSTANT")
                elif not s["binary"] and s["at_clamp"] >= SATURATION_FLAG:
                    notes.append(f"AT CLAMP {s['at_clamp']:.0%}")
                if notes:
                    findings.append(f"{behavior}/{s['name']}: {', '.join(notes)}")
                kind = "bin" if s["binary"] else "   "
                print(f"   {s['index']:2d} {s['name']:22} {kind} [{s['min']:+.2f}, {s['max']:+.2f}] "
                      f"mean {s['mean']:+.2f} sd {s['std']:.3f} clamp {s['at_clamp']:4.0%} "
                      f"{' '.join(notes)}")
            for pair in pairs:
                findings.append(f"{behavior}: {pair['a']} ~ {pair['b']} r={pair['r']:+.3f} (check: script habit?)")
                print(f"   CORRELATED {pair['a']} ~ {pair['b']}  r={pair['r']:+.3f}")

    print("\nFINDINGS" if findings else "\nNo clamped, constant or duplicated floats.")
    for line in findings:
        print("  " + line)

    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps(report, indent=1), encoding="utf-8")
        print(f"wrote {args.json}")

    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
