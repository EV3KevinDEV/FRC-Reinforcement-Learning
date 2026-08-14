from __future__ import annotations

import argparse
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
SERVER_EXECUTABLE = (
    REPOSITORY_ROOT / "_Build" / "RL" / "LinuxServer" / "MoSimRL.x86_64"
)
DEVELOPMENT_EXECUTABLE = (
    REPOSITORY_ROOT / "_Build" / "RL" / "LinuxDevelopment" / "MoSimRL.x86_64"
)


def positive_int(value: str) -> int:
    parsed = int(value)
    if parsed <= 0:
        raise argparse.ArgumentTypeError("value must be positive")
    return parsed


def executable_path(explicit: Path | None, *, graphical: bool) -> Path:
    path = explicit or (DEVELOPMENT_EXECUTABLE if graphical else SERVER_EXECUTABLE)
    path = path.expanduser().resolve()
    if not path.is_file():
        mode = "development" if graphical else "server"
        raise FileNotFoundError(
            f"Unity player not found at {path}. Build it with "
            f"scripts/build_unity.sh {mode}"
        )
    return path
