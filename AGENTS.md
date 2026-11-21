# AGENTS.md — PoFootball

## 1. Standard

> This repo owns every file in it. Nothing here is generated from, synced with, or required to stay identical to
> another repository. Only Azure resources in the `PoShared` resource group may be shared.

- **Engine:** Unity `6000.6.9f1` (`ProjectSettings/ProjectVersion.txt`), ML-Agents (`mlagents` 1.1.0).
- **Web page & chrome:** Title upper-left and version `vYYMMDD` lower-right drawn once outside the WebGL canvas; `/diag` live-probes dependencies in plain words; UI adapts to 16:9 landscape and 9:16 portrait; >= 4.5:1 text contrast.
- **In-game diagnostics & demo:** `TAB` (or touch corner button) opens FPS / episode reward / inference latency / curriculum stage; after 30 s idle, demo mode runs trained policy and cycles cameras until input resumes.
- **Editor-first config:** Reward weights, curriculum thresholds, cameras, and UI references live in ScriptableObjects / `[SerializeField]`.
- **Single workflow:** `.github/workflows/webgl.yml` deploys `webgl-*` releases to Azure Static Web Apps.
- **Repo-specific exception:** PoFootball is a 2D `Rigidbody2D` ML-Agents game (no 3D articulated skeleton or MuJoCo pipeline), and its current asset tree lives under `Assets/{Agents,Art,Scripts,Scenes,UI Toolkit}` rather than `Assets/_Project/`.

## 2. Concurrent work

- Only `master` exists locally and on GitHub.
- Before every commit and every push: `git fetch origin && git rebase --autostash origin/master`.
- Stage only your own files (`git add <paths>`); never `git add -A` / `git add .`; inspect `git diff --cached`.
- Keep recent owner decisions in commit history; never edit `.unity` scenes on disk while Unity Editor has them open.
- Ask before renaming or deleting Azure resources or rewriting git history.

## 3. Verification

| Check | Command | Notes |
| --- | --- | --- |
| Python lint | `uvx ruff check .` | Currently has legacy lint errors to clean up |
| Python tests | `uv run pytest -q` | Runs Python unit tests |
| Unity EditMode tests | `"C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe" -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults editmode.xml` | Requires Unity 6000.6.9f1 |
| WebGL publish | `pwsh -File Tools/publish_webgl.ps1` | Builds WebGL locally and publishes `webgl-*` release |
