from __future__ import annotations

import os
import signal
import socket
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import BinaryIO


@dataclass(slots=True)
class TensorBoardServer:
    log_dir: Path
    host: str = "127.0.0.1"
    port: int = 6006
    output_path: Path | None = None
    process: subprocess.Popen[bytes] | None = field(default=None, init=False)
    _output: BinaryIO | None = field(default=None, init=False, repr=False)

    @property
    def url(self) -> str:
        return f"http://{self.host}:{self.port}"

    @property
    def command(self) -> list[str]:
        return [
            sys.executable,
            "-m",
            "tensorboard.main",
            "--logdir",
            str(self.log_dir),
            "--host",
            self.host,
            "--port",
            str(self.port),
        ]

    def start(self, timeout: float = 15.0) -> None:
        if self.process is not None and self.process.poll() is None:
            return
        self.log_dir.mkdir(parents=True, exist_ok=True)
        output_path = self.output_path or self.log_dir / "tensorboard-server.log"
        output_path.parent.mkdir(parents=True, exist_ok=True)
        self._require_available_port()
        self._output = output_path.open("ab")
        try:
            self.process = subprocess.Popen(
                self.command,
                stdin=subprocess.DEVNULL,
                stdout=self._output,
                stderr=subprocess.STDOUT,
                start_new_session=True,
            )
            self._wait_until_ready(timeout)
        except BaseException:
            self.stop()
            raise

    def stop(self, timeout: float = 5.0) -> None:
        process, self.process = self.process, None
        try:
            if process is not None and process.poll() is None:
                try:
                    os.killpg(process.pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                else:
                    try:
                        process.wait(timeout=timeout)
                    except subprocess.TimeoutExpired:
                        os.killpg(process.pid, signal.SIGKILL)
                        process.wait(timeout=timeout)
        finally:
            if self._output is not None:
                self._output.close()
                self._output = None

    def _require_available_port(self) -> None:
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
                sock.bind((self.host, self.port))
        except OSError as error:
            raise RuntimeError(
                f"TensorBoard cannot start because {self.host}:{self.port} "
                "is already in use; choose another --tensorboard-port or pass "
                "--no-tensorboard"
            ) from error

    def _wait_until_ready(self, timeout: float) -> None:
        assert self.process is not None
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            return_code = self.process.poll()
            if return_code is not None:
                raise RuntimeError(
                    f"TensorBoard exited with code {return_code}; see "
                    f"{self.output_path or self.log_dir / 'tensorboard-server.log'}"
                )
            try:
                with socket.create_connection((self.host, self.port), timeout=0.25):
                    return
            except OSError:
                time.sleep(0.1)
        raise TimeoutError(
            f"TensorBoard did not listen on {self.host}:{self.port} within {timeout}s"
        )
