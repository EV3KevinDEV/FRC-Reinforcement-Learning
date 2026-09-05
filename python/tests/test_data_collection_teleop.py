from __future__ import annotations

import importlib.util
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest


pytest.importorskip("cv2")
pytest.importorskip("lerobot")

from lerobot.datasets.utils import validate_frame

from mosim_rl.constants import GAMEPAD_ACTION_DIM, NITROGEN_BUTTONS


SCRIPT = Path(__file__).resolve().parents[1] / "main" / "data_collection_teleop.py"
SPEC = importlib.util.spec_from_file_location("data_collection_teleop", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
collector = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(collector)


def test_data_collection_defaults_to_field_camera_and_drive() -> None:
    args = collector.parse_args([])
    assert args.camera_mode == "field"
    assert args.drive_mode == "field"
    assert not args.windowed_fullscreen
    assert args.episode_seconds == 156.0
    assert collector.FRAME_SKIP == 1
    assert collector.CONTROL_HZ == 50
    assert collector.FPS == 8
    assert args.dataset_fps == 8
    assert collector.build_reset_options(args) == {
        "camera_mode": "field",
        "drive_mode": "field",
        "scenario": "empty_start",
    }

    preload_args = collector.parse_args(["--with-preload"])
    assert collector.build_reset_options(preload_args) == {
        "camera_mode": "field",
        "drive_mode": "field",
    }

    fullscreen_args = collector.parse_args(["--windowed-fullscreen"])
    assert fullscreen_args.windowed_fullscreen


def test_data_collection_schema_keeps_all_gamepad_and_semantic_actions() -> None:
    cameras = {
        name: SimpleNamespace(width=32, height=24)
        for name in collector.CAMERA_FEATURES
    }
    features = collector.build_features(cameras)

    assert features["action"]["shape"] == (GAMEPAD_ACTION_DIM,)
    assert features["action"]["names"] == [
        "left_stick_x",
        "left_stick_y",
        "right_stick_x",
        "right_stick_y",
        *(name.lower() for name in NITROGEN_BUTTONS),
    ]
    assert features["action.semantic"]["shape"] == (6,)
    assert features["action.semantic"]["names"][3] == "target_setpoint"
    assert features["metadata.sample"]["shape"] == (2,)
    assert features["metadata.sample"]["names"] == [
        "sim_time_seconds",
        "control_udp_sequence",
    ]

    frame = {
        "observation.state": np.zeros(
            len(collector.LOCAL_STATE_NAMES), dtype=np.float32
        ),
        "action": np.zeros(GAMEPAD_ACTION_DIM, dtype=np.float32),
        "action.semantic": np.zeros(6, dtype=np.float32),
        "metadata.sample": np.zeros(2, dtype=np.float32),
        "metadata.capture": np.zeros(3, dtype=np.int64),
        "task": collector.TASK_DESCRIPTION,
    }
    for feature_name in collector.CAMERA_FEATURES.values():
        frame[feature_name] = np.zeros((24, 32, 3), dtype=np.uint8)

    validate_frame(frame, features)
