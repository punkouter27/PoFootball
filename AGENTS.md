# AGENTS.md — PoFootball

## 1. Coding standard

> This repo owns every file in it. Nothing here is generated from, synced with, or required to stay identical to
> another repository. Only Azure resources in the `PoShared` resource group may be shared.

- **Engine:** Unity `6000.6.9f1` (`ProjectSettings/ProjectVersion.txt`), ML-Agents C# 4.1.0 / Python `mlagents` 1.1.0.
- **Layout (Po Unity standard):**

  | Path | Holds |
  | --- | --- |
  | `Assets/_Project/Agents/{Agent,Sensor,Reward}` | `Agent_*`, `Sensor_*`, `Reward_*` scripts (PoFootball.Agents / Sensors / Rewards) |
  | `Assets/_Project/Core/Systems` (+ `Model`) | `Systems_*` rules, referee, game flow (PoFootball.Systems / Models — never ML-Agents) |
  | `Assets/_Project/Core/Editor`, `Core/Tests/{EditMode,PlayMode}` | `Editor_*` tools (menu root `Tools/PoFootball/`), the Unity test assemblies |
  | `Assets/_Project/Data/{Resources,Settings}` | ScriptableObjects and settings assets (panel settings, role shapes, brain table, reward penalty weights, showcase settings, URP) |
  | `Assets/_Project/Environments/Art` | shaders, shapes, fonts, exports |
  | `Assets/_Project/RL_Models/<Name>_v<NN>` | promoted `.onnx` brains + `MANIFEST.md` |
  | `Assets/_Project/Scenes` | `SCN_MENU`, `SCN_GAME`, `SCN_TRAIN_FOOTBALL` |
  | `Assets/_Project/UI/{View,Icons,UI Toolkit}` | `Systems_*` UI Toolkit screens and presentation (PoFootball.Views), app icons, theme |
  | `Training/` | `mlagents-learn` configs (`FootballBase31.yaml` is current; `archive/` will not run) |
  | `Tools/`, `tests/` | Python tooling (promotion gate, watchers, WebGL publish) and its pytest suite |

  Third-party code stays where Unity or its package puts it (`Assets/Plugins`, `Assets/BroAudio`, `Assets/ML-Agents`,
  `Assets/Packages`); `Assets/WebGLTemplates/PoFootball` must stay at that path. Move assets only with `git mv` of the
  asset **and** its `.meta`.
- **C#:** `.claude/rules/*.md` (MVS + VContainer + MessagePipe, `[SerializeField] private`, no LINQ/allocations in
  per-frame code, UI Toolkit built from C#, no legacy Input). 0 compiler warnings.
- **Python:** ruff (`pyproject.toml`), line length 120; `.venv` pins torch 2.5.1 / mlagents 1.1.0 (CLAUDE.md).
- **Web page & chrome:** the WebGL page (`Assets/WebGLTemplates/PoFootball`) has a header with the title upper-left
  and `/diag` upper-right, the canvas filling everything between, and a footer with `vYYMMDD` lower-right; the game
  does not draw its own title or version in a browser (`Systems_StatusHudView.PageDrawsChrome`). The canvas fills
  16:9 and 9:16 windows; in a landscape browser window the UI panel scales on height (`Systems_ScreenView`).
  `/diag` (`Tools/webgl/diag.html`, filled by `Tools/publish_webgl.ps1`) probes the page, the X-Commit header and
  every build file live from the visitor's browser; one probe round is kept 60 s in the tab (sessionStorage),
  `/diag?fresh` re-probes. The game sits in the page's `<main>`; the viewport allows pinch zoom (axe). Text
  contrast >= 4.5:1.
- **In-game diagnostics & demo:** `TAB` or the bottom-left chip (DEBUG in a development build, STATS in a release
  build) opens the RL readout: FPS, the last play's mean episode reward, the agent step per tick (which contains
  inference), the curriculum stage, the control penalties charged, and **BENCHMARK SIM SPEED** (3 s of this
  game's simulation as fast as the device can run it, labelled with the CPU/GPU it ran on; local sim speed, not a
  training run). After 30 s with no input the attract mode starts (`Systems_AttractModeView`): a CPU game from the
  menu, camera cycling broadcast -> follow -> orbit, ended by any key, click or tap. Its idle clock skips any frame
  over 0.25 s (load, stall, background tab; `Systems_AttractClockTests`).
- **Inference proof:** the first decision each promoted brain answers logs one
  `[PoFootball] <brain>: <model>.onnx inference running (...)` line (`Agent_InferenceLog`, Debug.Log on purpose so
  the release WebGL player shows it in the browser console).
- **Editor-first config:** control-penalty weights in `Data/Resources/PoFootballRewardPenalties.asset`
  (`Reward_PenaltyWeights`), attract/benchmark settings in `Data/Resources/PoFootballShowcase.asset`
  (`Systems_ShowcaseSettings`), cameras and lighting as `[SerializeField]` on the scene views, curriculum in the
  `Training/*.yaml` `environment_parameters`. The contract reward constants stay in `Systems_SimConstants`
  (the promotion gate and tests pin them).
- **Rewards:** progress, role shaping, call and terminal terms (CLAUDE.md), plus four control penalties
  (`Reward_Penalty`: torque/effort, angular velocity, action jitter, sliding across the facing) charged every live
  tick and logged per play as `Reward/<brain>/Penalty` plus one key per term
  `Reward/<brain>/Penalty/{Effort,AngularVelocity,ActionJitter,Slide}` (`Reward_PenaltyLedgerTests`). Every brain
  on disk predates the penalties; they shape the next run.
  No motion-capture data exists in this repo, so there is no BC/AMP pre-training stage (PPO/POCA only).
- **Single workflow:** `.github/workflows/webgl.yml` runs the Python checks on every push and deploys `webgl-*`
  releases to the Azure Static Web App, then checks the live X-Commit and `/diag`.
- **Known issue (open):** in the WebGL player every decision of a promoted brain throws
  `MethodAccessException: ... Unity.InferenceEngine.ITensorData.get_backendType` (seen on the live v261119 build
  and on v261122; a `link.xml` preserving the inference assemblies did not fix it). The game still plays.
- **Repo-specific exception:** PoFootball is a 2D `Rigidbody2D` ML-Agents game: no articulated skeleton, joints,
  PD actuators or head to gaze-track. Phone builds are portrait-locked; only the browser build adapts to landscape.

### Deliberate deviations

| Deviation | Why | Commit sha / date | How to undo |
| --- | --- | --- | --- |
| Layout moved to `Assets/_Project/...` + `/Training`, overriding the owner's 2026-11-21 note that kept the old `Assets/{Agents,Art,Scripts,Scenes,UI Toolkit}` tree | the v3 layout rule is newer; paths rewritten, GUIDs kept | `7863c72` 2026-11-22 | `git revert 7863c72` |
| 2D `Rigidbody2D` game: no articulated skeleton, PD actuators or gaze tracking | owner exception (physics rule N/A) | — | — |
| Phone builds stay portrait 9:16; only the browser build fills 16:9 (UI scales on height) | owner's portrait rule kept for phones; v3 16:9 rule for the web | `7863c72` 2026-11-22 | `Systems_ScreenView.ApplyBrowserAspect` |
| Unity build and tests run locally only; CI runs the Python checks and deploys the locally built `webgl-*` release | Unity cannot be licensed in CI (owner design) | `433cb63` 2026-11-22 | — |
| PlayMode runs with `-force-d3d11`, `CI=true`, `UNITY_MCP_KEEP_CONNECTED=false` | D3D12 device loss; the MCP bridge's auth error otherwise fails a random test | 2026-11-22 | — |
| Release WebGL shows only the STATS chip (RL readout); the findings sheet stays development-build only | owner gated the findings sheet | `7863c72` 2026-11-22 | — |

**Hosting:** Azure Static Web App `pofootball`, Free tier. Never run test suites against the live site; live checks =
at most 6 page loads + /diag + X-Commit.

## 2. Concurrent work

- Only `master` exists locally and on GitHub.
- Before every commit and every push: `git fetch origin && git rebase --autostash origin/master`.
- Stage only your own files (`git add <paths>`); never `git add -A` / `git add .`; inspect `git diff --cached`.
- Keep recent owner decisions in commit history; never edit `.unity` scenes on disk while Unity Editor has them open.
- One Unity instance (editor or batch mode) and one headless browser at a time; close them when done.
- Ask before renaming or deleting Azure resources or rewriting git history.

## 3. Verify the repo (exact commands, from the repo root)

One command sequence verifies the repo (Python checks, then EditMode and PlayMode in batch mode; close the Editor first):

```powershell
$env:CI = 'true'; $env:UNITY_MCP_KEEP_CONNECTED = 'false'; $u = "C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe"
uv run ruff format --check . && uv run ruff check . && uv run pytest -q && & $u -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -assemblyNames PoFootball.Tests.EditMode -testResults Logs/editmode.xml -logFile Logs/editmode.log && & $u -batchmode -force-d3d11 -projectPath . -runTests -testPlatform PlayMode -assemblyNames PoFootball.Tests.PlayMode -testResults Logs/playmode.xml -logFile Logs/playmode.log
```

Last full run (2026-11-23, three in a row): pytest 4 passed; EditMode 425/425; PlayMode batch 37 passed, 3 skipped
(the two aspect sweeps need the Editor's Game view, the final-tabs test the replay view); 0 `warning CS`.
The HUD tests spawn the player's HUD themselves in batch mode; the long showcase play tests wait for their condition
(capped) and carry `[Timeout(300000)]` instead of fixed windows and the framework's 180 s default.

Before any Unity test run, in the same PowerShell session: `$env:CI = 'true'; $env:UNITY_MCP_KEEP_CONNECTED = 'false'`.
The MCP bridge (`com.ivanmurzak.unity.mcp`) otherwise reconnects mid-run and its "Authorization failed" error log fails
whichever test is running at that moment.

| Check | Command | Expect |
| --- | --- | --- |
| Python format | `uv run ruff format --check .` | no file would be reformatted |
| Python lint | `uv run ruff check .` | All checks passed |
| Python tests | `uv run pytest -q` | all pass |
| Unity EditMode | `& "C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe" -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -assemblyNames PoFootball.Tests.EditMode -testResults Logs/editmode.xml -logFile Logs/editmode.log` | 0 failed, 0 `warning CS` in the log |
| Unity PlayMode | `& "C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe" -batchmode -force-d3d11 -projectPath . -runTests -testPlatform PlayMode -assemblyNames PoFootball.Tests.PlayMode -testResults Logs/playmode.xml -logFile Logs/playmode.log` | 0 failed, 3 skipped (Editor-only: aspect sweeps, final tabs); the HUD tests spawn the HUD themselves |
| Unity PlayMode, all tests | the same command without `-batchmode` (opens the Editor, runs, quits) | 0 failed, 0 skipped |
| WebGL build only | `pwsh -File Tools/publish_webgl.ps1 -NoPublish` | `WEBGL BUILD RESULT: Succeeded` in `Builds/webgl-build.log`; serve `Builds/WebGL` with `python -m http.server` |
| Publish + deploy | push first, then `pwsh -File Tools/publish_webgl.ps1` | a `webgl-*` release; the workflow run ends with `Live: ... at X-Commit <sha>` |
