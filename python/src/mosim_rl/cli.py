from __future__ import annotations

import os
from pathlib import Path

import yaml


def default_executable() -> Path:
    configured = os.environ.get("MOSIM_EXECUTABLE")
    if configured:
        return Path(configured).expanduser().resolve()
    repository_root = Path(__file__).resolve().parents[3]
    return repository_root / "_Build" / "RL" / "LinuxServer" / "MoSimRL.x86_64"


def development_executable() -> Path:
    repository_root = Path(__file__).resolve().parents[3]
    return repository_root / "_Build" / "RL" / "LinuxDevelopment" / "MoSimRL.x86_64"


def positive_int(value: str) -> int:
    parsed = int(value)
    if parsed <= 0:
        raise ValueError("value must be positive")
    return parsed


def selected_num_envs() -> int:
    configured = os.environ.get("MOSIM_NUM_ENVS")
    if configured:
        return positive_int(configured)
    config_path = Path(__file__).resolve().parents[2] / "config" / "runtime.yaml"
    if config_path.is_file():
        data = yaml.safe_load(config_path.read_text()) or {}
        return positive_int(str(data.get("selected_workers", 8)))
    return 8
