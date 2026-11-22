# Football_v05

Promoted 2026-11-18 from `results/football_base33` at repository commit `fa6cdd1`.

## Contract

- Vector observations: **50**
- Ray observations: **84**
- Continuous actions: **2** (**4** for `Quarterback`)
- Discrete branches: **[8, 2]** on `Quarterback`, none elsewhere

Every file below was validated against these numbers by `Tools/promote_brain.py` before being copied. A brain that does not match is refused, not warned about.

## Run

- Run id: `football_base33`
- `--num-envs`: **4** — changes how experience is batched; runs with different values are not comparable
- Judge on `Call/Entropy` and the `Play/*` and `Pass/*` rates — self-play is not running, so there is no `Self-play/ELO` — then on the REALISM section below (`--check-realism`). Mean reward is zero-sum and stays near 0 however strong the policies get (UNITY_RULES §4).
- `Control/SpeedClampRate` was at most 0.001 for every brain.

## Files

| Brain | Source checkpoint |
|---|---|
| `Defense.onnx` | `results/football_base33/Defense/Defense-685553.onnx` |
| `DefenseCall.onnx` | `results/football_base33/DefenseCall/DefenseCall-922.onnx` |
| `Offense.onnx` | `results/football_base33/Offense/Offense-623230.onnx` |
| `Quarterback.onnx` | `results/football_base33/Quarterback/Quarterback-62323.onnx` |
