from __future__ import annotations

import sys
from pathlib import Path

from mosim_rl.tensorboard_server import TensorBoardServer


def test_tensorboard_command_uses_current_python_and_configured_listener(
    tmp_path: Path,
) -> None:
    server = TensorBoardServer(tmp_path, host="127.0.0.1", port=7007)

    assert server.command == [
        sys.executable,
        "-m",
        "tensorboard.main",
        "--logdir",
        str(tmp_path),
        "--host",
        "127.0.0.1",
        "--port",
        "7007",
    ]


def test_tensorboard_url_matches_listener(tmp_path: Path) -> None:
    server = TensorBoardServer(tmp_path, host="localhost", port=6006)

    assert server.url == "http://localhost:6006"
