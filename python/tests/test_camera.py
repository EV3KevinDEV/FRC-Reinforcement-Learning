from __future__ import annotations

import base64

import pytest

from mosim_rl.camera import VirtualCameraFrame, VirtualCameraInfo


def test_camera_info_parses_robot_relative_calibration() -> None:
    info = VirtualCameraInfo.from_payload(
        {
            "name": "front",
            "width": 320,
            "height": 180,
            "vertical_fov_degrees": 70.0,
            "near_clip": 0.03,
            "far_clip": 50.0,
            "robot_position": [0.0, 0.5, 0.4],
            "robot_rotation_euler": [0.0, 15.0, 0.0],
        }
    )

    assert info.name == "front"
    assert info.robot_position == (0.0, 0.5, 0.4)
    assert info.robot_rotation_euler == (0.0, 15.0, 0.0)


def test_camera_frame_decodes_and_saves_jpeg(tmp_path) -> None:
    jpeg = b"\xff\xd8virtual-camera\xff\xd9"
    frame = VirtualCameraFrame.from_payload(
        {
            "name": "front",
            "width": 320,
            "height": 180,
            "encoding": "jpeg",
            "media_type": "image/jpeg",
            "image_base64": base64.b64encode(jpeg).decode("ascii"),
            "sequence": 4,
            "sim_time": 1.25,
        }
    )

    destination = frame.save(tmp_path / "nested" / "frame.jpg")
    assert frame.image_bytes == jpeg
    assert destination.read_bytes() == jpeg


def test_camera_frame_rejects_invalid_base64() -> None:
    with pytest.raises(ValueError, match="valid base64"):
        VirtualCameraFrame.from_payload(
            {
                "name": "front",
                "width": 320,
                "height": 180,
                "encoding": "jpeg",
                "media_type": "image/jpeg",
                "image_base64": "%%not-base64%%",
                "sequence": 1,
                "sim_time": 0.0,
            }
        )
