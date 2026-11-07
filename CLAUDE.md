# CLAUDE.md

@AGENTS.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# PoFootball

2D American football simulation. Players are flat 2D shapes whose geometry encodes
position (QB, receiver, lineman, defender, …); the game simulates a football game
being played rather than being directly controlled. Behaviour is learned with
Unity ML-Agents self-play.

| Property | Value |
|---|---|
| Unity | 6000.6.0f1 |
| Render pipeline | URP 17.6.0 |
| ML-Agents (C#) | `com.unity.ml-agents` 4.1.0 — comms API **1.5.0** |
| ML-Agents (Python) | `mlagents` 1.1.0 — comms API **1.5.0** |
| Python | 3.10.11, venv at `.venv/` |
| Torch | 2.5.1+cu121 — **do not upgrade**; 2.11 breaks the `.onnx` export |
| GPU | RTX 5070 Ti Laptop (Blackwell, sm_120) — unused; the CPU is 6x faster |

---

# UNITY_RULES — ML-Agents Physics Simulation

## 1. Naming
- `Assets/Agents/<Name>_v<NN>/` — `.onnx`, `*_Character.asset`, `MANIFEST.md`.
- Four script prefixes, each matching its folder under `Assets/Scripts/`: `Agent_`,
  `Sensor_`, `Reward_`, `Systems_` (referees, presentation, UI — the largest).
  `Assets/Editor/` follows the same folder-matching rule: everything there is
  `Editor_`, and every menu item hangs off the single root `Tools/PoFootball/`.
- Scenes `SCN_`; training `SCN_TRAIN_<NAME>`, no suffixes. Envs `Builds/<Name>Env/`;
  configs `<Name><Phase><NN>.yaml` paired 1:1 with run-id `<name>_<phase><nn>`.

## 2. Physics & Biomechanics
- Earth gravity (−9.81), SI units, realistic friction, deterministic execution.
- Actions apply only in `FixedUpdate`. Lock Δt = 0.02 s and solver iterations — an override silently changes the dynamics every brain was fitted against.
- Normalize actions to [−1, 1], scaled to real joint limits and DoF.
- Fatigue reads load from **applied torque, not the action vector** — isometric bracing is a near-zero action at near-maximum torque. Clear it on reset *before* restoring motors.
- Anchors derive from segment lengths — move a segment, everything above it moves.
- Verify joint ranges **parent-local**: gravity off, the body counter-rotates and a
  world-space test reads drift. Measure the `jointAngle` sign.

## 3. Display
- UI Toolkit only — no UGUI, no IMGUI, no `.uxml`/`.uss`. Screens built from C# at runtime.
- Portrait 9:16, 60 FPS (`vSyncCount = 0` or `targetFrameRate` is ignored).
- **One status HUD, five corners, every player-facing screen** — title top-left, FPS
  top-centre, MENU top-right, DEBUG bottom-left, `Application.version` bottom-right.
  It is spawned by `Systems_StatusHudBootstrap`, not authored in the scenes, so the
  two screens cannot drift apart; `Systems_UiTheme.STATUS_BAR_HEIGHT` /
  `STATUS_FOOTER_HEIGHT` are the strips every other screen must offset by. The
  version stamp used to be top-left of `SCN_MENU` alone — it is bottom-right on
  *both* screens now, which is more of what that rule was asking for, not less.
  Everything is on an inset layer, outside any ScrollView, and non-pickable but the
  two chips.
- The DEBUG sheet is written in **English, worst finding first** — what is wrong,
  how bad, what to do — not rows of instrument readings. The readings are under it.
- The DEBUG sheet opens from its chip, TAB, or a three-finger tap; its PHYSICS
  switch draws colliders, velocity, facing and fatigue on the field
  (`Systems_PhysicsDebugView`, debug builds, game scenes only).
- **Android Back** (the Escape key) closes the topmost sheet or reel, then takes
  the MENU chip's way out: two presses to abandon a live game, one to leave a
  finished one, two to close the app from the menu. One route,
  `Systems_StatusHudView.RouteBack`; a screen opts in with `HandleBack`.
- No control in the top 40% of the screen but the MENU chip
  (`Systems_UiTheme.REACH_EXEMPT_CLASS`). `Systems_AspectSweepTests` holds that,
  and no cut-off, overflowing or scrolling text, on five ratios from 16:9 to 21:9.
- The result screen is one sheet with three tabs — BOX SCORE, SCORING, HIGHLIGHTS.
- The panel scales on width — size against a live capture
  (*Tools → PoFootball → UI → Capture Screens (5 ratios)*).

## 4. MLOps
- C# and Python `mlagents` versions must match; comms API version **equal** or the
  handshake is refused.
- Overwrite `.onnx` in place to preserve `.meta` GUIDs.
- Headless: `--env --no-graphics` + explicit `--base-port` (envs take consecutive ports; collisions hang). 4–8 envs, leaving cores for torch. `--num-envs` changes how experience is batched — record it.
- Telemetry over HTTP *and* `StatsRecorder` (`Agent_Telemetry`, a component on `/Systems` in `SCN_TRAIN_FOOTBALL` only). Kill TensorBoard before `--force`: it holds Windows handles and the wipe silently no-ops. Clean up trainer → envs → TensorBoard.
- **Judge self-play on ELO, not mean reward** — *when self-play is actually running*. It is not, in any `FootballBase` config from base05 onward, so there is no `Self-play/ELO` for those; judge them on `Call/Entropy` and the play mix. The reason [docs/TRAINING_NOTES_base05.md](docs/TRAINING_NOTES_base05.md) gives for removing it — that ML-Agents self-play cannot model two different behaviors playing each other — is **wrong**: `GhostTrainer` handles one behavior per team, and the official `StrikersVsGoalie.yaml` is exactly that with `poca`. `Config/FootballSelf01.yaml` switches it back on as an experiment against the `football_base19` baseline; its header has the settings, the likely real cause of the base04 stall, and why its ELO is a trend and not a win rate.

---

## How these rules land in this repo

**Layout** (created; `Assets/Scripts/<X>/` holds the `<X>_*.cs` files):

```
Assets/Agents/                 promoted brains: <Name>_v<NN>/{*.onnx, *_Character.asset, MANIFEST.md}
                               — Football_v03 (rev 16) and v04 (rev 17), both refused; see below
Assets/Scripts/Agent/          Agent_*.cs      — Agent subclasses, action application
Assets/Scripts/Sensor/         Sensor_*.cs     — observation collection
Assets/Scripts/Reward/         Reward_*.cs     — reward terms
Assets/Scripts/Systems/        Systems_*.cs    — referee, game flow, UI (no ML-Agents dependency)
Assets/Scripts/Systems/View/   Systems_*.cs    — UI Toolkit screens, audio, shape presentation
Assets/Scenes/                 SCN_*.unity, SCN_TRAIN_*.unity — these three only
Assets/Resources/              PoFootballPanelSettings, PoFootballRoleShapes, M_PoFootball*.mat
                               — no PoFootballBrains yet; Build Brain Table writes it
Builds/<Name>Env/              headless training envs (git-ignored) — FootballEnv only
Config/<Name><Phase><NN>.yaml  trainer configs, 1:1 with run-id <name>_<phase><nn>
Config/archive/                configs for superseded contracts; they will NOT run
results/<run-id>/MANIFEST.md   one per run — records --num-envs, which is part of the run's identity
```

**Current state (2026-11-07). CONTRACT REVISION 18; NO BRAIN LOADS; every player
runs `Heuristic`.** Revision 18 moved the quarterback's call latch, and so the first
legal throw, from 0.8 s to 1.5 s after the snap (`Systems_SimConstants.DROPBACK_TICKS`
40 -> 75): Football_v04 threw a median 1.0 s after the snap, before any rush could
arrive, so no run ever sacked anyone. `Agent_ActionContract` has the detail and
[docs/CONTRACT_HISTORY.md](docs/CONTRACT_HISTORY.md) has the revisions before 17.

- **`Config/FootballBase31.yaml` is the config to run** (run-id `football_base31`):
  every brain warm-started from Football_v04's own checkpoints (football_base28 at
  100k), no reward change, so the realism line reads the latch alone.
- **`Assets/Agents/Football_v04`** (football_base28 at 100k, revision 17) is in the
  brain table and passed 4 of 5 realism bands (rush 2.29 yd/carry), but the stamp
  refuses it in this revision 18 build. Revision 17's runs: base25-27 never threw;
  base28 threw (v04); base29/30 charged the fouler his own flag (`Reward_Terminal
  .OwnFouls`) and cut flags to ~3%, yet base30 FAILED realism at 2.4M and 4.3M
  (`results/football_base30/MANIFEST.md`). Revision 17 is promotable only from
  commit a48dc7b.
- `Tools/run_unattended.py` now starts the trainer through a shim that re-enables
  CTRL+C: base30, launched from an agent's shell, inherited "ignore CTRL+C", trained
  4 h past a watcher HALT and was killed without exporting. Its end-of-run realism
  check still needs the Editor (`--realism-in-editor`, or run
  `Tools/checkpoint_realism.py --in-editor` afterwards).
- **`Assets/Agents/Football_v03`** (from `football_base22`, revision 16) is on disk
  and committed, is NOT in a brain table and FAILED the realism gate on all five bands.
- A run from an older revision is promoted only from the commit it was trained at;
  [docs/CONTRACT_HISTORY.md](docs/CONTRACT_HISTORY.md) names each one.

To promote: train against a config for the CURRENT contract revision, run
`Tools/promote_brain.py`, then **`Tools > PoFootball > Build Brain Table`** in the
Editor. That last step is not optional — the Python side copies `.onnx` files but
cannot write the ScriptableObject that lists them. `Agent_BrainTable
.MatchesCurrentContract` checks the group set as well as the revision stamp, so a
table cannot be made to load by re-stamping it by hand.

Every config for an older contract is in `Config/archive/`.

**Judge balance on fixed seeds.** Games with a pinned seed (`Systems_GameLifetimeScope
._varySeedPerGame` off, `_episodeSeed` set) replay exactly, and today's single
games ranged 6.7-13.1 yards a play on unchanged code, so comparisons of one game
each are noise. Compare means over the same four seeds.

Note that `m_Model` references serialized in the scenes are dead either way:
`Agent_FootballPlayer` assigns `behaviorParameters.Model` from
`Agent_BrainRegistry` at `Awake`, overwriting whatever the scene held.

Heuristic-only is a supported, playable state, not a bug — but nothing you watch
right now is a trained policy. **To change that, train and promote `football_base31`**
— see above.

**QB CHALLENGE (2026-10-23)** — the one-player mode, from the menu's QB CHALLENGE
button. The human is the home quarterback for one 180 s period
(`Systems_GameLength.QbChallenge`: no halftime, no overtime, football's clock rules for
both sides) and scores as many points as he can; the computer plays his ten team-mates,
the whole defense, every kick and every snap his team is not on offense for. Before each
of his downs `Systems_QbChallengeSystem` deals 9 of the 20 plays in `Systems_Playbook`
(plus PUNT, and FIELD GOAL in range, on 4th down) and holds the snap
(`Systems_ISnapGate`, shut before the pre-snap count starts, so the kick window still
works) until he picks one and taps the field. A play is a formation, a job per eligible
slot (`Systems_Route`, geometry in `Systems_RouteBook`) and a designed call — but the real
call latches only when he acts, so any play can be thrown or handed off.
**Controls are tap to run, double-tap to pass, identical for touch and mouse**
(`Systems_HumanControlModel.Tap`, 0.35 s / 3 m window, on unscaled time): a tap on the
field is where the ball carrier sprints to, stopping on the spot (so a runner who stops
in space is ruled down, as the stall rule says); a tap ON a team-mate (within 1.2 m:
at 2 m a drop-back tap behind the quarterback pitched to the fullback standing there)
beside or behind him is a handoff (a back within reach) or a pitch; a tap in the
pre-snap count after the hike is where he drops to; a carrier nobody has tapped for
keeps the run he took the ball on. A double tap is a PASS TO THAT SPOT
(`Systems_BallSystem.ThrowToSpot`), not to a man: speed from the distance at the normal
loft, the climb that lands it exactly there, the usual throw error, nobody can catch it
above `SPOT_PASS_CATCH_HEIGHT` (the ball's height is real for this pass only), it falls
incomplete where it lands, and team-mates in reach break for the landing spot, defenders
after a 0.3 s read (breaking at the release, they met every catch: 4.5, 0.3, 0.0 and -2.7
yards). The hike's tap never pairs into a double. Hovering the mouse only draws the ring;
it steers nobody. Training and the CPU game are unchanged. The best score is kept in
PlayerPrefs. The variant crosses the
scene load like the seed (`Systems_GameVariantRequest`). Scripted routes exist only for
designed plays, so the CPU-only game still runs its old receiver fan.
Tuned from played games (2026-10-23; the scripted human went 0-10, 0-16, then 21-0
after): a defender holds an interception of the human's pass 25% of the time, not 60%
(`QB_CHALLENGE_INTERCEPTION_HOLD_CHANCE`: 6 of 13 throws were picked off before, 0 of
14 after); his downs run to 1500 ticks, not 600 (`Systems_PlayModel.TickCeiling`); no
drawn flinch or delay of game on his snap (`Systems_ISnapGate.IsHumanSnap`); a ring
marks the team-mate a tap would hand or pitch to and a dot the spot the carrier is
running to or the pass is coming down on; the call sheet says YOU instead of the
script's odds. For every game, a pass in the air is drawn bigger as it
climbs, lifted 1:1 and tethered to its shadow (`Systems_BallView`; presentation only).

**Scenes.** `SCN_MENU` (front end) → `SCN_GAME` (a scored game) and
`SCN_TRAIN_FOOTBALL` (the trainer's endless single plays). All three share one
composition root; `Systems_GameLifetimeScope._simMode` decides whether the
scoreboard, chains and stats are in the container at all.

**`PoFootball.Systems` must never reference `Unity.ML-Agents`.** Training-only
concerns reach it through interfaces the Systems assembly owns —
`Systems_ITrainingHandle` (terminal reward, end of episode),
`Systems_ITrainingEpisodeBoundary` (bound to a no-op in Game mode) and
`Systems_IInjectableBehaviour` (how `Agent_Telemetry` gets injected without the
scope naming it). Adding the reference back would put the trainer inside the
assembly that is supposed to know nothing about training, and ship ML-Agents in a
retail build's gameplay code — which is exactly what `Systems_Telemetry` did
before it became `Agent_Telemetry`.

The line of scrimmage is the ONLY thing that differs between the two modes, and
it is behind `Systems_ISpotProvider` — training still draws it from the same
seeded RNG, call for call. See [docs/GAME_LAYER.md](docs/GAME_LAYER.md).

**Presentation** — the stadium light rig and 2D shadows, the post-processing
stack, the diagnostic overlay, the broadcast microphone and the UI typography all
live in `PoFootball.Views` and are built at runtime, gated on
`Systems_PresentationBudget`. That class gates **construction, not playback**: a
view that says no must not build its rig at all, because the cost a training sweep
must not pay is the setup. Why each is shaped the way it is — and why this project
deliberately has no sprite atlas and no `AudioMixer` asset — is in
[docs/PRESENTATION.md](docs/PRESENTATION.md).

**Physics** is 2D here (`Rigidbody2D`), so §2's joint/biomechanics clauses apply to
whatever articulated shapes get built; the invariants that always hold are:
fixed Δt = 0.02, actions only in `FixedUpdate`, actions normalized to [−1, 1],
fatigue derived from applied force/torque rather than the action vector — since
revision 12 that includes the contact impulse, which is what makes it true of a
braced lineman. §2's "clear it on reset" is `RestBetweenPlays` here: called before
the body is restored, as the rule says, but it keeps
`FATIGUE_CARRIED_BETWEEN_PLAYS` of the fatigue rather than zeroing it, so a drive
costs something.
`Time.fixedDeltaTime` is pinned in project settings — do not override it at runtime
or from a trainer flag, or every existing `.onnx` is being evaluated against
different dynamics than it was fitted against.

**On a fresh clone, before anything else:** `git lfs pull`. 72 files are in LFS,
including every DLL under `Assets/Plugins/NuGet`; without them the MCP bridge fails
to compile with `CS0234 ... 'ReflectorNet'`, and because the Editor will not reload
a domain that has compile errors, **every later script change compiles to disk and
is silently never loaded** — tests keep passing against the old assemblies. Then
recreate `.venv` (the two commands at the top of `requirements.txt`).

**Training** — the config and run-id are paired by name:

```powershell
.venv\Scripts\Activate.ps1

# In-editor smoke test: start the trainer, then press Play.
$env:CUDA_VISIBLE_DEVICES = "-1"     # MANDATORY on this machine. See below.
mlagents-learn Config\FootballBase26.yaml --run-id=football_base26

# Headless sweep — envs take CONSECUTIVE ports from --base-port.
# REBUILD Builds/FootballEnv FIRST whenever the contract revision moved:
#   Unity: Tools > PoFootball > Build Training Env
# Revisions 8 and 9 changed the DYNAMICS with every shape left identical, so a
# stale env is NOT always refused by the handshake. That rebuild is on you.
# --num-envs=4: measured 257.7 / 255.1 / 236.3 steps/s at 2 / 4 / 12 envs
# (rl_optimization_log.md); the trainer is the bottleneck.
mlagents-learn Config\FootballBase26.yaml --run-id=football_base26 `
  --env=Builds\FootballEnv\PoFootball.exe --no-graphics `
  --base-port=5400 --num-envs=4

tensorboard --logdir results
```

### Judging whether the SIMULATION is football

`Systems_GameFlowSystem` logs a verdict line at every final whistle:

```
[PoFootball] REALISM  yards/play 6.24 | 4th downs faced 10 | TD/drive 0.36 | scrimmage plays 71 | rush yd/carry 4.10 | first downs 22 | completion 0.64
```

Reference values from real football: **~5.5 yards a play, a fourth down on roughly
one series in three, and 0.20-0.35 touchdowns per drive** — and, since 2026-10-16,
**~4.3 yards a carry, ~65% completions and a first down on ~0.3 of scrimmage
snaps**. The last three exist because a scripted game read 6.4 yards a play out of
runs that LOST two yards and completions that went seventy: the mean was football's
and the plays were not.

Balance changes are judged on that line and nothing else — four were reasoned about
confidently during the revision 8 work and two of them made the game measurably
worse. **Single games are very noisy** (4.48 to 7.37 yards/play on one config), so
compare means over the same seeds.

`Tools > PoFootball > Evaluate Realism (4 fixed seeds)` (`Editor_RealismEval`) plays the
four FIXED seeds 1-4 at 8x — the same four games on the same code, every run — and
writes `results/realism/realism-<stamp>.json`; headless, run
`Unity.exe -batchmode -projectPath . -executeMethod
PoFootball.EditorTools.Editor_RealismEval.RunBatch` without `-quit`.
`Tools/promote_brain.py --version NN --check-realism <file>` judges the means
(yards/play 4.5-6.5, TD/drive 0.15-0.40, rush yd/carry 3.0-6.0, completion
0.50-0.80, first downs per scrimmage play 0.15-0.45; a report written before the
last three were logged is judged on the first two) and records the verdict in that
version's MANIFEST.md.

The scripted players FAIL the new bands. On revision 15, four fixed seeds:

| scripted build | yd/play | TD/drive | yd/carry | completion | 1st downs/play |
|---|---|---|---|---|---|
| merge as committed (`realism-20261016-095716.json`) | 5.22 | 0.24 | -2.70 | 0.65 | 0.07 |
| + line fires out, drives on runs; no dropback on a run | 8.63 | 0.30 | -0.37 | 0.61 | 0.08 |
| + defenders aim at a pass's path (`realism-20261016-101114.json`) | 7.48 | 0.29 | -0.65 | 0.57 | 0.05 |

The two heuristic changes fix real defects (a line that gave ground on every run,
defenders converging on where a thrown ball HAD been) and were kept, but the
scripted game is still a run game that loses ground and a pass game that goes the
distance. A trained revision 15 brain, gated on these bands, is the way out.

**Calibration and audits (2026-10-22).** `Tools/calibrate_dynamics.py` times every
role's 10- and 40-yard sprint from the C# constants, with Box2D's integrator, which
`Systems_PhysicsContractTests.SprintSplit_MatchesTheIntegratorThisToolModels` pins
against real Physics2D. On revision 16/17 constants **every role is FAR OFF the combine**:
skill players run 10 yards in ~2.0 s (combine ~1.55) and 40 in 5.2 s (4.5); tight ends,
fullbacks, quarterbacks and both lines have a top speed BELOW their forty's average
speed, so no damping fixes them. A joint fit puts every role near damping 1.4-1.6 with
slightly higher top speeds (`results/audit/calibration-rev16.json`) — close to the
1.5 revision 3 abandoned as "fast-forward". NOT CHANGED: it is a balance change,
judged on the realism line only, and damping also sets how hard a body pushes.
TRIED 2026-10-23 on the revision 17 scripted game (damping 1.5, WR/CB 9.6, S 9.4,
RB 9.5, LB 9.1, TE/FB 8.9, QB 8.8, lines 7.4 at 2.4): 2 of 5 bands against 3 of 5
for the committed constants (`realism-20261023-024023.json` vs `-023435`) — yards a
play 4.94 -> 7.24, out of band; rush 1.12 -> 1.81 and first downs 0.02 -> 0.05 a play,
both still out. Reverted. The heuristic was tuned against the old sprint, so a
trained brain, not the script, is the fair test of it.
`Tools > PoFootball > Audit > Record Observations` (`Editor_ObservationAudit`) records
every brain's observations as ML-Agents `.demo` files and `Tools/audit_observations.py`
reports clamped, constant and duplicated floats; it has not been run on real data yet
(the Editor was closed for base24).

`Tools > PoFootball > Sim Speed > 8x` (editor-only, `Assets/Editor/Editor_SimSpeed.cs`)
turns a full game from ~10 minutes into ~100 seconds. It raises `Time.timeScale`
only — `fixedDeltaTime` is untouched, so it is the same measurement — and it always
resets to 1x on leaving play mode.

**Torch is pinned at 2.5.1+cu121 and upgrading it breaks promotion.** 2.11.0+cu128
was tried: it trained happily for 80,000 steps, then died at the first checkpoint with
`ModuleNotFoundError: No module named 'onnxscript'`. torch >= 2.6 exports ONNX via
onnxscript, which pulls `onnx>=1.17` -> numpy 2.x and protobuf 7.x, against the
`onnx==1.15.0` / `numpy==1.23.5` / `protobuf==3.20.3` that mlagents 1.1.0 requires. A
trainer that cannot write an `.onnx` cannot feed `Tools/promote_brain.py`, so the
faster-looking option is the useless one.

**Train on the CPU, and pass `CUDA_VISIBLE_DEVICES=-1` to make that stick.**

Measured on this machine, `OffenseSkill` steps 20k -> 60k, headless env:

| setup | steps/s |
|---|---|
| Editor, 1 env | 243 |
| headless, 3 envs | 395 |
| headless, 6 envs | 337 |
| headless, 12 envs | 404 |
| headless, 12 envs, `hidden_units: 256` | 563 |
| headless, 24 envs | **fails** — paging file too small |
| headless, 6 envs, **on the GPU** | **55** |

Two things fall out of that. The GPU is 6x *slower* — the net is 512 x 2 at batch
2048, so launch overhead and host transfers cost more than the matmuls save. And
env count barely matters, because the trainer is the bottleneck, not the game: at 6
envs the six `PoFootball.exe` copies burned 81 CPU-seconds while python burned 2,877.
Use a handful of envs and do not expect much from adding more. 24 envs does not run
at all — each worker loads its own copy of torch and Windows runs out of paging file.

`--torch-device=cpu` and `torch_settings: device: cpu` do NOT work on their own. Both
were tried against live runs and both still died on CUDA. `mlagents/torch_utils/torch.py`
calls `set_torch_config(TorchSettings(device=None))` at **import** time; with no device
given it picks `"cuda"` whenever a GPU is visible and calls
`torch.set_default_device("cuda")`. When the real config arrives moments later asking
for `cpu`, the `else` branch only sets the dtype — it never puts the default device
back. Hiding the GPU is what fixes it. Use `-1`; an empty string is ignored on Windows.

`Config/archive/FootballBase06.yaml` (long superseded — the live config is
`FootballBase26.yaml`, four behaviors, revision 17) carried **six** behaviors. The quarterback has its own brain
— it is the only one with discrete actions, and while it shared `OffenseSkill`
with the backs and receivers its play-call gradient was diluted five to one and
its entropy bonus could not be raised without injecting noise into four other
players' steering.

Record `--num-envs` in the run's `MANIFEST.md` — it changes how experience is
batched, so two runs with the same YAML and different `--num-envs` are not
comparable. Before `--force`, kill TensorBoard: it holds Windows file handles and
the wipe silently no-ops. Shut down in order: trainer → envs → TensorBoard.

Judge a self-play run on the `Self-play/ELO` curve — but no current config runs
self-play (UNITY_RULES §4), so from base05 on there is no ELO to read. Mean reward
is zero-sum across offense and defense and stays near 0 however strong the policy
becomes.
Watch `Call/Entropy` alongside it: the trainer's own `Policy/Entropy` sums across
every action head at once and sat at a healthy 3.5 through `football_base03`
while the play call had collapsed onto one option on 93% of downs.

**Promotion is gated.** An `.onnx` outlives the code it was trained against and
the runtime loads a mismatched one without complaint, so never copy one in by
hand:

```powershell
# audit what is already in Assets/Agents/ against the current contract
.venv\Scripts\python.exe Tools\promote_brain.py --verify

# promote — refuses unless every brain's observation and action shapes match,
# then writes MANIFEST.md and overwrites in place to preserve .meta GUIDs
.venv\Scripts\python.exe Tools\promote_brain.py --run football_base06 `
  --version 01 --num-envs 6

# reclaim disk: keeps the final and peak-ELO checkpoint per brain, never events
.venv\Scripts\python.exe Tools\prune_results.py --apply

# watch a live run's play-call entropy; exits nonzero the moment it collapses
.venv\Scripts\python.exe Tools\watch_entropy.py --run football_base06

# watch a live run for every reward hack this project has paid for once (stall,
# flags, solver blow-ups, no sacks, touchdown flood, call or front collapse,
# shaping outgrowing the result); --stop-trainer sends CTRL+C on a HALT
.venv\Scripts\python.exe Tools\watch_hacks.py --run football_base26 --once

# AFTER a run: play the 4 fixed-seed realism games with each of the newest
# checkpoints (Editor_CheckpointRealism; never touches the promoted table) and
# rank them, so the brain promoted is the checkpoint that plays football
.venv\Scripts\python.exe Tools\checkpoint_realism.py --run football_base26 --last 4
```

The gate reads every expected value out of `Sensor_FootballState.cs`,
`Sensor_RayContract.cs`, `Agent_ActionContract.cs` and `Systems_RoleTable.cs`, so it
cannot drift from the contract it guards. (The ray width came from the training
scene until revision 10; `Agent_FootballPlayer` now applies `Sensor_RayContract`
over whatever the scenes say.) It also refuses a run whose `Control/SpeedClampRate`
averaged above 0.001 over its last five summaries — a policy exploiting solver
blow-ups — and `--check-realism` judges the promoted brains' REALISM line. The deleted `Assets/Agents/Football_v01`
is the reason it exists: those four brains came from `football_base02` at ~500k
steps against a contract that had since changed, the shipped `OffenseSkill.onnx`
had no discrete output at all, and the resulting "quarterback calls the same play
every down" was diagnosed as a collapsed policy for a long time. Its provenance is
recorded in [results/football_base02/MANIFEST.md](results/football_base02/MANIFEST.md).

---

## Tooling

Scene and GameObject changes go through **MCP tools**, not hand-edited `.unity`
YAML and not bespoke Editor scripts. Unity CLI (`unity` + `com.unity.pipeline`) is
the preferred surface; see [docs/TOOLING.md](docs/TOOLING.md) for the full
inventory and the precedence order.

- `unity status` — is an Editor connected?
- `unity list` / `unity command` — tools the Pipeline package exposes.
- `unity test` / `unity build` — batch-mode runs.

**Running tests** (detail and failure modes in [docs/TOOLING.md](docs/TOOLING.md)).
Always scope to this project's assemblies — unfiltered is 370 tests, 232 of them
from `com.besty.unity-skills`, one of which fails for unrelated reasons:

```powershell
# EditMode (Assets/Tests/EditMode). Blocks the Editor's main thread — pass a long timeout.
unity command --timeout 600 run_tests --mode editor --filter PoFootball --filter_type assembly
# PlayMode (Assets/Tests/PlayMode). Must be async; poll test_status until "completed".
unity command run_tests --mode playmode --async_tests --filter PoFootball --filter_type assembly
```

`--assembly X` is silently ignored and runs everything; the flag pair above is the
filter. Never start a run while play mode is active. Check the Editor actually
reloaded the domain first (see the LFS note above) — a stale domain passes tests
against old assemblies.

**Assembly layering** (asmdefs enforce it; a new reference that breaks it will not compile):

```
PoFootball.Models    (Systems/Model)  — pure data, references nothing
PoFootball.Systems   → Models                      — rules, referee, game flow; NO ML-Agents
PoFootball.Views     → Models, Systems             — UI Toolkit, audio, presentation
PoFootball.Rewards   → Models
PoFootball.Sensors   → Models, Unity.ML-Agents
PoFootball.Agents    → Models, Systems, Sensors, Rewards, Unity.ML-Agents
```

Agents is the only assembly that sees everything; Systems reaches training only
through the `Systems_I*` interfaces described above.

`.claude/` ships 20 agents, 27 commands, 42 skills and 26 hooks from
everything-claude-unity. The hooks block direct `.unity`/`.meta` edits on purpose —
that is the rule above being enforced, not an obstacle to route around.

---

## Android release & Play internal testing

Ported from the PoRacer pipeline on 2026-08-28; PoSumo is the original, already
shipping on the punkouter27 Play account.

### Identity (permanent — do not change after the first upload)

| Property | Value |
|---|---|
| Application id | `com.punkoutersoftware.pofootball` |
| Version / code | `1.0.0` / `7` at time of writing — both Android builders bump the code once per build (`Editor_BuildAndroidAAB.NextVersionCode`); `Editor_ConfigureAndroidRelease` only enforces a floor of 1 and never lowers it. Play rejects a reused code |
| min / target SDK | 26 / 36 (Play requires target 36 for new uploads from 2026-08-31) |
| Architecture | ARM64, IL2CPP, Release |
| Orientation | Portrait is locked in `Editor_ConfigureAndroidRelease`. |

### Secrets live OUTSIDE the repo

`C:/Users/punko/OneDrive/VAULT/_CODE/` — the shared vault every Punkouter app's
signing key was consolidated into on 2026-09-02. Files are flat and app-prefixed;
`KEYSTORES-README.txt` there is the authority on the layout and the pinning rules.
The old `Downloads/PoFootball-Release/` in this document never existed on disk, and
the builders pointed at it until 2026-09-09, which is why every Android build
aborted on "keystore not found".

- `pofootball-upload.jks` — the upload key. **NOT CREATED YET.** Nothing has been
  uploaded to Play under this application id, so it is still safe to create; the
  moment it is, losing it means losing the ability to update the app. One command,
  and it must be run by a human because generating signing material is exactly the
  action an agent should not be doing unattended:

  ```powershell
  $vault = "C:\Users\punko\OneDrive\VAULT\_CODE"
  $pass  = -join ((48..57)+(65..90)+(97..122) | Get-Random -Count 28 | % {[char]$_})
  [IO.File]::WriteAllText("$vault\pofootball-upload.pass", $pass)
  & "C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot\bin\keytool.exe" `
      -genkeypair -v -keystore "$vault\pofootball-upload.jks" -storetype JKS `
      -alias pofootball-upload -keyalg RSA -keysize 2048 -validity 10950 `
      -storepass $pass -keypass $pass `
      -dname "CN=Punkouter Software, O=Punkouter Software, C=US"
  attrib +P -U "$vault\pofootball-upload.jks" "$vault\pofootball-upload.pass"
  ```

- `pofootball-upload.pass` — the store/alias password, one line, no trailing newline.
- `pofootball-upload.pem` — the public cert, for Play App Signing.
- `pofootball-play-service-account.json` — NOT created yet; see the SETUP block at
  the top of `Tools/play_publish.py`.

Unity does not serialize keystore passwords into `ProjectSettings`, so both Android
builders read `POFOOTBALL_KEYSTORE_PASS` first and fall back to the `.pass` file.
Without either, the **release** APK and the AAB abort rather than produce an
artifact that cannot update a Play install. The **development** APK does not: it
falls back to the Android SDK debug key, because Play rejects a debuggable bundle
outright, so the upload key buys a test-handset build nothing and demanding it only
blocks the one artifact whose job is to get onto a phone today. That build logs a
`BUILD SIGNING:` warning saying which key it used.

### The tools

| Tool | What it does |
|---|---|
| *Tools → PoFootball → Configure Android Release Settings* | One-shot: identity, SDK levels, orientation, and the launcher icons (adaptive + round + legacy, 6 densities) from `Assets/Icons/`. Re-run after changing icon art |
| *Tools → PoFootball → Build Android AAB (Play release)* | Signed bundle → `Builds/Android/PoFootball.aab`. Logs `AAB BUILD RESULT:` |
| *Tools → PoFootball → Build Android APK* | Sideloadable APK on the SAME key, so it installs over a Play build → `Builds/Android/PoFootball.apk`. Logs `BUILD RESULT:` |
| *Tools → PoFootball → Build Android APK (development)* | The same APK with Development Build on — the only artifact where `Debug.isDebugBuild` is true and therefore the only one whose DEBUG sheet appears. This is what goes on a test handset |
| `Tools/play_publish.py` | Uploads a built AAB. Defaults to the `internal` track as a `draft`; `--dry-run` rehearses and discards |
| `Tools/publish.ps1` | Headless AAB build + `play_publish.py` in one command, with vault credentials. See `Tools/PUBLISHING.md` (moved from `Scripts/`) |

`Tools/play_publish.py` needs its own venv (`Tools/publish-venv`). Do not install it
into `.venv` — that one carries load-bearing ml-agents/torch pins, and the C#/Python
ml-agents versions must stay in exact parity.

### The shipped scene list is explicit

`Editor_BuildAndroidAAB.SHIP_SCENES` names the player's scenes in boot order:

  0. `Assets/Scenes/SCN_MENU.unity`
  1. `Assets/Scenes/SCN_GAME.unity`

It is a hardcoded list, not whatever is ticked in Build Settings, because Build
Settings also carries SCN_TRAIN_FOOTBALL — training scenes that would bloat the bundle
and, depending on order, boot a tester straight into a training rig. A scene named
here that is missing on disk **aborts** the build.

### The icons

`Assets/Icons/` holds `AppIcon_Adaptive_Background.png` and
`AppIcon_Adaptive_Foreground.png` (432x432, the API 26+ pair) and
`AppIcon_Legacy.png` (512x512, round and pre-adaptive launchers). The adaptive
FOREGROUND art must stay inside the middle 66% of its canvas — every OEM launcher
masks the outside to a different shape.

The Play STORE icon is a different file, in `StoreAssets/PlayStoreIcon_512.png`:
full-bleed, because Play rounds it itself. Do not swap the two.

### What still needs a human in a browser

1. Play Console → Create app, with the application id above.
2. Store listing, content rating and data-safety forms — drafted in
   `StoreAssets/play-listing.md`.
3. Upload the first bundle by hand; Play refuses an API upload before the app is set up.
4. Create a service account, grant it release permission ON THE APP, and drop its
   JSON key next to the keystore.

After that, `python Tools/play_publish.py --track internal` owns every upload.

### Headless

```
Unity.exe -batchmode -quit -nographics -projectPath <root> -buildTarget Android ^
  -executeMethod PoFootball.EditorTools.Editor_BuildAndroidAAB.Build -logFile <log>
```

Grep the log for `AAB BUILD RESULT:` — that line is the outcome.
