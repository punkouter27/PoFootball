# Football_v04

Promoted 2026-11-05 from `results/football_base28` at repository commit `d4acf83`.

## Contract

- Vector observations: **50**
- Ray observations: **84**
- Continuous actions: **2** (**4** for `Quarterback`)
- Discrete branches: **[8, 2]** on `Quarterback`, none elsewhere

Every file below was validated against these numbers by `Tools/promote_brain.py` before being copied. A brain that does not match is refused, not warned about.

## Run

- Run id: `football_base28`
- `--num-envs`: **4** — changes how experience is batched; runs with different values are not comparable
- Judge on `Call/Entropy` and the `Play/*` and `Pass/*` rates — self-play is not running, so there is no `Self-play/ELO` — then on the REALISM section below (`--check-realism`). Mean reward is zero-sum and stays near 0 however strong the policies get (UNITY_RULES §4).
- `Control/SpeedClampRate` was at most 0.001 for every brain.

## Files

| Brain | Source checkpoint |
|---|---|
| `Defense.onnx` | `results/football_base28/final_weights/best_realism_99986/Defense.onnx` |
| `DefenseCall.onnx` | `results/football_base28/final_weights/best_realism_99986/DefenseCall.onnx` |
| `Offense.onnx` | `results/football_base28/final_weights/best_realism_99986/Offense.onnx` |
| `Quarterback.onnx` | `results/football_base28/final_weights/best_realism_99986/Quarterback.onnx` |

## Realism

Judged 2026-11-05 from `results/realism/realism-20261105-191549.json`, 4 games: **FAILED**

- yards per play: **5.08** (band 4.50-6.50) ok
- touchdowns per drive: **0.32** (band 0.15-0.40) ok
- rush yards per carry: **2.29** (band 3.00-6.00) OUT OF BAND
- completion rate: **0.62** (band 0.50-0.80) ok
- first downs per play: **0.21** (band 0.15-0.45) ok
- fourth downs per game: **11.0** (reported, not judged)
