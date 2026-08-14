from __future__ import annotations

import os
import signal
import shutil
import socket
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path

import psutil

from .constants import DEFAULT_HOST

WINDOWS_CREATE_NEW_PROCESS_GROUP = getattr(
    subprocess, "CREATE_NEW_PROCESS_GROUP", 0x00000200
)


def _is_windows() -> bool:
    return sys.platform == "win32"


def reserve_tcp_port(host: str = DEFAULT_HOST) -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind((host, 0))
        return int(sock.getsockname()[1])


def require_free_space(path: Path, minimum_gib: float = 25.0) -> None:
    free = shutil.disk_usage(path).free
    required = int(minimum_gib * 1024**3)
    if free < required:
        raise RuntimeError(
            f"MoSimulator setup requires {minimum_gib:.0f} GiB free; "
            f"only {free / 1024**3:.1f} GiB is available at {path}"
        )


@dataclass(slots=True)
class UnityWorkerProcess:
    executable: Path
    worker_id: int
    port: int
    log_dir: Path
    seed: int
    job_worker_count: int = 2
    graphical: bool = False
    realtime: bool = False
    process: subprocess.Popen[bytes] | None = None

    @property
    def log_path(self) -> Path:
        return self.log_dir / f"worker-{self.worker_id}.log"

    def start(self) -> None:
        if self.process is not None and self.process.poll() is None:
            return
        if not self.executable.is_file():
            raise FileNotFoundError(f"Unity player not found: {self.executable}")
        self.log_dir.mkdir(parents=True, exist_ok=True)
        command = [str(self.executable)]
        if self.graphical:
            if sys.platform.startswith("linux"):
                command.append("-force-glcore")
            command.extend(
                [
                    "-screen-fullscreen",
                    "0",
                    "-screen-width",
                    "1280",
                    "-screen-height",
                    "720",
                    "--rl-graphical",
                ]
            )
        else:
            command.extend(["-batchmode", "-nographics"])
        command.extend(
            [
                "-silent-crashes",
                "-logFile",
                str(self.log_path),
                "-job-worker-count",
                str(self.job_worker_count),
                "--rl",
                "--rl-host",
                DEFAULT_HOST,
                "--rl-port",
                str(self.port),
                "--rl-worker-id",
                str(self.worker_id),
                "--rl-seed",
                str(self.seed),
            ]
        )
        if self.realtime:
            command.append("--rl-realtime")
        env = os.environ.copy()
        env.setdefault("SDL_AUDIODRIVER", "dummy")
        process_group_options = (
            {"creationflags": WINDOWS_CREATE_NEW_PROCESS_GROUP}
            if _is_windows()
            else {"start_new_session": True}
        )
        self.process = subprocess.Popen(
            command,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            env=env,
            **process_group_options,
        )

    def assert_running(self) -> None:
        if self.process is None:
            raise RuntimeError("Unity worker has not been launched")
        return_code = self.process.poll()
        if return_code is not None:
            tail = ""
            if self.log_path.exists():
                tail = "\n".join(
                    self.log_path.read_text(errors="replace").splitlines()[-30:]
                )
            raise RuntimeError(
                f"Unity worker {self.worker_id} exited with code {return_code}\n{tail}"
            )

    def stop(self, timeout: float = 10.0) -> None:
        process, self.process = self.process, None
        if process is None or process.poll() is not None:
            return
        if _is_windows():
            try:
                process.terminate()
            except ProcessLookupError:
                return
        else:
            try:
                os.killpg(process.pid, signal.SIGTERM)
            except ProcessLookupError:
                return
        try:
            process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            if _is_windows():
                process.kill()
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=5.0)

    @property
    def rss_bytes(self) -> int:
        if self.process is None or self.process.poll() is not None:
            return 0
        try:
            process = psutil.Process(self.process.pid)
            return process.memory_info().rss + sum(
                child.memory_info().rss for child in process.children(recursive=True)
            )
        except (psutil.Error, OSError):
            return 0


def wait_for_tcp_server(
    host: str,
    port: int,
    *,
    timeout: float,
    worker: UnityWorkerProcess | None = None,
) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if worker is not None:
            worker.assert_running()
        try:
            with socket.create_connection((host, port), timeout=0.5):
                return
        except OSError:
            time.sleep(0.1)
    raise TimeoutError(
        f"Unity worker did not listen on {host}:{port} within {timeout}s"
    )
