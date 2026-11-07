"""Run (or resume) one training run unattended for a fixed number of hours, then
judge it and write its report.

    .venv\\Scripts\\python.exe Tools\\run_unattended.py --run football_base26 `
        --config Config\\FootballBase26.yaml --hours 8 --resume --stall-after 1500000

WHAT IT DOES, IN ORDER
----------------------
1. Starts mlagents-learn headless (CUDA_VISIBLE_DEVICES=-1, --num-envs, --base-port)
   in a console of its own, so it can be sent CTRL+C, and Tools/watch_hacks.py
   alongside it with --stop-trainer.
2. Every minute: a trainer that died WITHOUT a watcher HALT is restarted with
   --resume (it picks up from its last checkpoint; at most --max-restarts times).
   A HALT is respected: the run is over and nothing restarts it. A file named
   STOP_TRAINING in the project root ends the run early, cleanly.
3. At the deadline: CTRL+C, so the trainer exports its final .onnx files, then the
   shutdown order CLAUDE.md section 4 gives — trainer, envs, TensorBoard.
4. Tools/checkpoint_realism.py --last N on the newest checkpoints (batch Unity;
   the four fixed-seed games each), the final and best-ranked weights copied to
   results/<run>/final_weights/, and Tools/training_report.py.

It never PROMOTES. Promotion is gated on the realism bands and ends in Build Brain
Table in the Editor and a commit; that is a person's decision, made from the report.

Everything it does is logged to results/<run>/unattended.log.
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
PYTHON = REPO_ROOT / ".venv" / "Scripts" / "python.exe"
ENV_EXE = REPO_ROOT / "Builds" / "FootballEnv" / "PoFootball.exe"
STOP_FILE = REPO_ROOT / "STOP_TRAINING"
BRAINS = ("Offense", "Defense", "Quarterback", "DefenseCall")

# Re-enables CTRL+C in the trainer, whatever its parent ignored, then runs
# mlagents-learn's own entry point (mlagents.trainers.learn:main).
TRAINER_SHIM = ("import ctypes, sys; ctypes.windll.kernel32.SetConsoleCtrlHandler(None, False); "
                "sys.argv[0] = 'mlagents-learn'; from mlagents.trainers.learn import main; sys.exit(main())")

CREATE_NEW_CONSOLE = 0x00000010
STARTF_USESHOWWINDOW = 0x00000001
SW_HIDE = 0


class Run:
    def __init__(self, args):
        self.args = args
        self.dir = REPO_ROOT / "results" / args.run
        # NOT created here: mlagents-learn refuses a fresh run whose results/<run>
        # already exists (football_base27's first launch). Until the trainer makes
        # it, lines go to a pending file beside it and are folded in afterwards.
        self.log_path = self.dir / "unattended.log"
        self.pending_log = REPO_ROOT / "results" / f"{args.run}_unattended.pending.log"
        self.trainer = None
        self.watcher = None
        self.restarts = 0
        self.ended_by = None

    def log(self, message: str) -> None:
        line = f"[{time.strftime('%Y-%m-%d %H:%M:%S')}] {message}"
        print(line, flush=True)
        if not self.dir.is_dir():
            with open(self.pending_log, "a", encoding="utf-8") as handle:
                handle.write(line + "\n")
            return
        if self.pending_log.exists():
            with open(self.log_path, "a", encoding="utf-8") as handle:
                handle.write(self.pending_log.read_text(encoding="utf-8"))
            self.pending_log.unlink()
        with open(self.log_path, "a", encoding="utf-8") as handle:
            handle.write(line + "\n")

    def _hidden_console(self):
        info = subprocess.STARTUPINFO()
        info.dwFlags |= STARTF_USESHOWWINDOW
        info.wShowWindow = SW_HIDE
        return info

    def start_trainer(self, resume: bool) -> None:
        env = dict(os.environ, CUDA_VISIBLE_DEVICES="-1", PYTHONUNBUFFERED="1")
        # mlagents-learn through TRAINER_SHIM, not mlagents-learn.exe: a process started from
        # a shell that ignores CTRL+C inherits that, and football_base30 (launched by
        # an agent's shell) ignored both the watcher's HALT and the deadline's CTRL+C,
        # trained 4 h past the HALT, and was killed without exporting its brains.
        command = [str(PYTHON), "-u", "-c", TRAINER_SHIM, self.args.config, f"--run-id={self.args.run}",
                   f"--env={ENV_EXE}", "--no-graphics",
                   f"--base-port={self.args.base_port}", f"--num-envs={self.args.num_envs}"]
        if resume:
            command.append("--resume")
        out = open(REPO_ROOT / "results" / f"{self.args.run}_trainer.log", "a", encoding="utf-8")
        # A console of its own: CTRL+C is delivered per console, and the trainer
        # must get it without this process getting it too.
        self.trainer = subprocess.Popen(
            command, cwd=REPO_ROOT, env=env, stdout=out, stderr=subprocess.STDOUT,
            creationflags=CREATE_NEW_CONSOLE, startupinfo=self._hidden_console())
        self.log(f"trainer started, pid {self.trainer.pid}{' (--resume)' if resume else ''}")
        self.start_watcher()

    def start_watcher(self) -> None:
        if self.watcher and self.watcher.poll() is None:
            self.watcher.kill()
        command = [str(PYTHON), "-u", str(REPO_ROOT / "Tools" / "watch_hacks.py"),
                   "--run", self.args.run, "--stop-trainer", "--trainer-pid", str(self.trainer.pid),
                   "--target", str(10**12)]
        if self.args.stall_after:
            command += ["--stall-after", str(self.args.stall_after)]
        out = open(REPO_ROOT / "results" / f"{self.args.run}_watch.log", "a", encoding="utf-8")
        self.watcher = subprocess.Popen(command, cwd=REPO_ROOT, stdout=out, stderr=subprocess.STDOUT,
                                        creationflags=subprocess.CREATE_NO_WINDOW)
        self.log(f"hack watcher started, pid {self.watcher.pid}")

    def stop_trainer_cleanly(self) -> None:
        if not self.trainer or self.trainer.poll() is not None:
            return
        # watch_hacks.stop_trainer attaches to the trainer's console; run it in a
        # child so THIS process never gives up its own.
        subprocess.run([str(PYTHON), "-c",
                        "import sys; sys.path.insert(0, r'%s'); import watch_hacks; "
                        "watch_hacks.stop_trainer(%d)" % (REPO_ROOT / "Tools", self.trainer.pid)],
                       cwd=REPO_ROOT, creationflags=subprocess.CREATE_NO_WINDOW)
        self.log("CTRL+C sent; waiting for the trainer to export its brains")
        try:
            self.trainer.wait(timeout=600)
        except subprocess.TimeoutExpired:
            self.log("trainer did not exit within 10 min; killing it")
            self.trainer.kill()

    def train(self) -> None:
        deadline = time.time() + self.args.hours * 3600
        self.log(f"training {self.args.run} until {time.strftime('%Y-%m-%d %H:%M', time.localtime(deadline))}")
        self.start_trainer(resume=self.args.resume)

        while True:
            time.sleep(60)

            if STOP_FILE.exists():
                self.ended_by = "STOP_TRAINING file"
                self.stop_trainer_cleanly()
                break

            if time.time() >= deadline:
                self.ended_by = f"deadline ({self.args.hours:g} h)"
                self.stop_trainer_cleanly()
                break

            if self.trainer.poll() is None:
                if self.watcher.poll() == 1:
                    # A HALT the trainer has not acted on within a minute: stop it
                    # here (CTRL+C again, then kill after 10 min) rather than let it
                    # train on past the rule that halted it.
                    self.ended_by = "hack watcher HALT (see the watch log)"
                    self.log("watcher HALTed but the trainer is still running; stopping it")
                    self.stop_trainer_cleanly()
                    break
                if self.watcher.poll() not in (None, 1):
                    self.log(f"watcher exited {self.watcher.returncode}; restarting it")
                    self.start_watcher()
                continue

            if self.watcher.poll() == 1:
                self.ended_by = "hack watcher HALT (see the watch log)"
                self.log("trainer stopped by a watcher HALT; not restarting")
                break

            self.restarts += 1
            self.log(f"trainer exited {self.trainer.returncode} on its own (restart {self.restarts})")
            if self.restarts > self.args.max_restarts:
                self.ended_by = f"trainer died {self.restarts} times"
                break
            time.sleep(20)  # let the envs release their ports
            kill_envs(self)
            self.start_trainer(resume=True)

        if self.watcher and self.watcher.poll() is None:
            self.watcher.kill()
        kill_envs(self)
        self.log(f"training ended: {self.ended_by}")

    def judge_and_report(self) -> None:
        self.dir.mkdir(parents=True, exist_ok=True)
        if self.args.tensorboard_pid:
            subprocess.run(["taskkill", "/PID", str(self.args.tensorboard_pid), "/T", "/F"],
                           capture_output=True)
            self.log("TensorBoard stopped")

        if self.args.realism_last > 0:
            self.log(f"checkpoint realism on the newest {self.args.realism_last} checkpoints")
            with open(self.dir / "checkpoint_realism.log", "a", encoding="utf-8") as out:
                code = subprocess.run(
                    [str(PYTHON), "-u", str(REPO_ROOT / "Tools" / "checkpoint_realism.py"),
                     "--run", self.args.run, "--last", str(self.args.realism_last)]
                    + (["--in-editor"] if self.args.realism_in_editor else []),
                    cwd=REPO_ROOT, stdout=out, stderr=subprocess.STDOUT).returncode
            self.log(f"checkpoint realism exited {code}")

        stage_weights(self)

        code = subprocess.run([str(PYTHON), str(REPO_ROOT / "Tools" / "training_report.py"),
                               "--run", self.args.run], cwd=REPO_ROOT,
                              capture_output=True, text=True)
        self.log(f"training report: {(code.stdout or code.stderr).strip()}")


def kill_envs(run: Run) -> None:
    """Envs the trainer left behind hold their ports, and the next handshake hangs."""
    result = subprocess.run(["taskkill", "/IM", "PoFootball.exe", "/F"], capture_output=True, text=True)
    if "SUCCESS" in result.stdout:
        run.log("stopped leftover PoFootball.exe envs")


def stage_weights(run: Run) -> None:
    """The trainer's final export, and the checkpoint the realism games ranked best."""
    target = run.dir / "final_weights"
    target.mkdir(exist_ok=True)
    copied = []
    for brain in BRAINS:
        source = run.dir / f"{brain}.onnx"
        if source.exists():
            shutil.copy2(source, target / f"{brain}.onnx")
            copied.append(source.name)

    ledger = run.dir / "realism_checkpoints.json"
    if ledger.exists():
        sys.path.insert(0, str(REPO_ROOT / "Tools"))
        from checkpoint_realism import distance_from_bands  # the tool's own ranking

        judged = [e for e in json.loads(ledger.read_text(encoding="utf-8")) if e.get("means")]
        if judged:
            judged.sort(key=lambda e: (not e["passed"], distance_from_bands(e["means"])))
            best = judged[0]
            best_dir = target / ("best_realism_" + best["label"].split("@")[-1])
            best_dir.mkdir(exist_ok=True)
            for brain, name in best.get("files", {}).items():
                source = run.dir / brain / name
                if source.exists():
                    shutil.copy2(source, best_dir / f"{brain}.onnx")
            (best_dir / "verdict.json").write_text(json.dumps(best, indent=2), encoding="utf-8")
            copied.append(f"{best_dir.name} ({'PASSED' if best['passed'] else 'failed'} the realism bands)")
    run.log(f"weights staged in {target.relative_to(REPO_ROOT)}: {', '.join(copied) or 'nothing'}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--run", required=True)
    parser.add_argument("--config", required=True)
    parser.add_argument("--hours", type=float, default=8.0)
    parser.add_argument("--resume", action="store_true", help="the run exists; continue it")
    parser.add_argument("--num-envs", type=int, default=4)
    parser.add_argument("--base-port", type=int, default=5400)
    parser.add_argument("--stall-after", type=int, default=0, help="passed to watch_hacks.py")
    parser.add_argument("--max-restarts", type=int, default=5)
    parser.add_argument("--realism-last", type=int, default=4,
                        help="checkpoints to judge at the end; 0 skips it")
    parser.add_argument("--realism-in-editor", action="store_true",
                        help="judge checkpoints in the OPEN Editor; the batch-mode path "
                             "never finished a game for football_base26 (2026-11-05)")
    parser.add_argument("--tensorboard-pid", type=int, default=0,
                        help="stopped after the trainer and envs, per CLAUDE.md")
    args = parser.parse_args()

    if not ENV_EXE.exists():
        print(f"No env at {ENV_EXE}; build it first", file=sys.stderr)
        return 2
    if STOP_FILE.exists():
        print(f"{STOP_FILE.name} exists; delete it to start", file=sys.stderr)
        return 2

    run = Run(args)
    try:
        run.train()
    finally:
        if run.trainer and run.trainer.poll() is None:
            run.stop_trainer_cleanly()
        kill_envs(run)
    run.judge_and_report()
    run.log("done")
    return 0


if __name__ == "__main__":
    sys.exit(main())
