"""PoFootball Workload: mlagents-learn on the newest Training/FootballBase*.yaml, 4 headless env players on the CPU,
as Tools/run_unattended.py runs it (CUDA_VISIBLE_DEVICES=-1, --num-envs 4, --no-graphics).

Run by serve.py with the training venv:  .venv/Scripts/python.exe benchmark/workload.py
Benchmark player: Builds/FootballEnv/PoFootball.exe, built here in Unity batch mode on first use (Unity Editor closed).

The config is used as written except that every behavior's init_path (a warm start from a local results/ run) is
dropped: a timing run must not depend on which checkpoints this machine happens to have, and a warm start does not
change the cost of a step. The trimmed copy is results/_benchmark.yaml.

Hooks: TrainerController.advance = one env-manager step (sim) followed by every trainer's advance (learn: trajectory
processing and, once per full buffer, the POCA/PPO update). Warm-up ends after the first update of any brain; the
window then ends on a later update so it always holds whole collect-then-update cycles. A Step = every env player
advancing once.

Deliberately NO TensorBoard: this is a ~10 s timing run that is never judged or promoted, and a TensorBoard process
would add load that skews the number. Its run dir results/_benchmark is throwaway (--force; results/ is gitignored).
"""

from __future__ import annotations

import os

os.environ["CUDA_VISIBLE_DEVICES"] = "-1"  # before torch loads: CLAUDE.md trains on the CPU (6x faster than the GPU)

import datetime  # noqa: E402
import re  # noqa: E402
import subprocess  # noqa: E402
import sys  # noqa: E402
import time  # noqa: E402
from importlib import metadata  # noqa: E402
from pathlib import Path  # noqa: E402

sys.path.insert(0, str(Path(__file__).resolve().parent))
import core  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
PLAYER = ROOT / "Builds" / "FootballEnv" / "PoFootball.exe"
BUILD_METHOD = "PoFootball.EditorTools.Editor_BuildMenu.BuildTrainingEnvHeadless"
NUM_ENVS = 4  # Tools/run_unattended.py default
BASE_PORT = 5450  # clear of run_unattended.py's 5400-5403, so a live training run is not disturbed
RUN_ID = "_benchmark"
RESULTS = ROOT / "results"
TRIMMED = RESULTS / "_benchmark.yaml"


def latest_config() -> Path:
    configs = sorted((ROOT / "Training").glob("FootballBase*.yaml"), key=lambda p: int(re.sub(r"\D", "", p.stem) or 0))
    if not configs:
        raise RuntimeError("no Training/FootballBase*.yaml")
    return configs[-1]


def version(pkg: str) -> str | None:
    try:
        return metadata.version(pkg)
    except metadata.PackageNotFoundError:
        return None


def unity_version() -> str:
    text = (ROOT / "ProjectSettings" / "ProjectVersion.txt").read_text()
    return text.split("m_EditorVersion:")[1].split()[0]


def build_player() -> None:
    """Build the training env in Unity batch mode (first use only; needs the Unity Editor closed)."""
    unity = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Unity/Hub/Editor" / unity_version()
    unity = unity / "Editor/Unity.exe"
    if not unity.exists():
        raise RuntimeError(f"Unity {unity_version()} is not installed (needed once to build {PLAYER.name})")
    core.say("building the training env Builds/FootballEnv (first run on this machine, several minutes)")
    log = ROOT / "Logs" / "benchmark_build.log"
    log.parent.mkdir(exist_ok=True)
    args = ["-batchmode", "-nographics", "-quit", "-projectPath", str(ROOT), "-executeMethod", BUILD_METHOD]
    subprocess.run([str(unity), *args, "-logFile", str(log)], check=False)
    text = log.read_text(encoding="utf-8", errors="replace") if log.exists() else ""
    if not PLAYER.exists() or "[Build] Training env ->" not in text:
        hint = "see Logs/benchmark_build.log"
        if "No valid Unity Editor license" in text:
            hint = "Unity licence not active - sign in to Unity Hub, then run the Benchmark again"
        elif "another Unity instance" in text:
            hint = "close the Unity Editor (it holds the project lock) and retry"
        raise RuntimeError(f"training env build failed: {hint}")


def trimmed_config(config: Path) -> dict:
    """The config minus warm starts, written to results/_benchmark.yaml; returns its behaviors."""
    import yaml

    data = yaml.safe_load(config.read_text(encoding="utf-8"))
    for b in data["behaviors"].values():
        b.pop("init_path", None)
    RESULTS.mkdir(exist_ok=True)
    TRIMMED.write_text(yaml.safe_dump(data, sort_keys=False), encoding="utf-8")
    return data["behaviors"]


def workload(config: Path, behaviors: dict) -> dict:
    built = datetime.datetime.fromtimestamp(PLAYER.stat().st_mtime).isoformat(timespec="seconds")
    return dict(
        repo="PoFootball",
        trainer=f"mlagents {version('mlagents')} (torch {version('torch')}, CPU)",
        config=f"Training/{config.name} (init_path dropped)",
        config_hash=core.workload_id(behaviors),
        num_envs=NUM_ENVS,
        behaviors={
            name: dict(
                trainer=b["trainer_type"],
                batch=b["hyperparameters"]["batch_size"],
                buffer=b["hyperparameters"]["buffer_size"],
                net=[b["network_settings"]["hidden_units"]] * b["network_settings"]["num_layers"],
                time_horizon=b.get("time_horizon"),
            )
            for name, b in behaviors.items()
        },
        unity=unity_version(),
        player_built=built,
    )


def main() -> None:
    from mlagents.trainers import learn
    from mlagents.trainers.trainer.on_policy_trainer import OnPolicyTrainer
    from mlagents.trainers.trainer.rl_trainer import RLTrainer
    from mlagents.trainers.trainer_controller import TrainerController

    meter = None
    config = None
    try:
        config = latest_config()
        if not PLAYER.exists():
            build_player()
        core.T_PROCESS = time.time()  # Startup time excludes the one-time player build
        behaviors = trimmed_config(config)
        versions = dict(python=sys.version.split()[0], torch=version("torch"), mlagents=version("mlagents"))
        meter = core.Meter(workload(config, behaviors), versions, warmup_blocks=None)
    except Exception as e:  # noqa: BLE001 - every failure becomes a failed row
        name = config.name if config else "Training/FootballBase*.yaml"
        meter = meter or core.Meter({"repo": "PoFootball", "config": name}, {"python": sys.version.split()[0]})
        meter.finish(ok=False, error=core.fail_text(e))

    state = {"learn": 0.0, "updated": False}
    trainer_advance, update_policy, tc_advance = RLTrainer.advance, OnPolicyTrainer._update_policy, None

    def trainer_advance_(self):
        t = time.perf_counter()
        trainer_advance(self)
        state["learn"] += time.perf_counter() - t

    def update_policy_(self):
        done = update_policy(self)
        state["updated"] = True
        meter.end_warmup()  # no-op once measuring; the call holding this update is still warm-up (see below)
        return done

    tc_advance = TrainerController.advance

    def tc_advance_(self, env_manager):
        if not meter.on_exit:
            meter.on_exit.append(env_manager.close)  # shut the Unity players down before the process ends
        was_measuring = meter.measuring
        state["learn"], state["updated"] = 0.0, False
        t = time.perf_counter()
        n = tc_advance(self, env_manager)
        total = time.perf_counter() - t
        if was_measuring:
            meter.block(total - state["learn"], state["learn"], n / NUM_ENVS)
        return n

    meter.can_finish = lambda: state["updated"]
    RLTrainer.advance, OnPolicyTrainer._update_policy, TrainerController.advance = (
        trainer_advance_,
        update_policy_,
        tc_advance_,
    )
    sys.argv = [
        "mlagents-learn",
        str(TRIMMED),
        f"--run-id={RUN_ID}",
        f"--results-dir={RESULTS}",
        f"--env={PLAYER}",
        f"--num-envs={NUM_ENVS}",
        "--no-graphics",
        f"--base-port={BASE_PORT}",
        "--force",
    ]
    os.chdir(ROOT)
    try:
        learn.main()
        meter.finish(ok=False, error="training ended before the measured window finished")
    except BaseException as e:  # noqa: BLE001
        meter.finish(ok=False, error=core.fail_text(e))


if __name__ == "__main__":
    main()
