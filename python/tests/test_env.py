from __future__ import annotations

import base64
from copy import deepcopy

import numpy as np
import pytest

from mosim_rl.env import MoSimEnv
from mosim_rl.constants import GAMEPAD_ACTION_DIM
from mosim_rl.gamepad import BUTTON_INDEX


class FakeClient:
    def __init__(self, state: dict) -> None:
        self.state = state
        self.last_command: str | None = None
        self.last_payload: dict | None = None
        self.raise_timeout = False
        self.closed = False

    def request(self, command: str, payload: dict, **kwargs) -> dict:
        self.last_command = command
        self.last_payload = payload
        return {"state": deepcopy(self.state), "events": {}, "info": {}}

    def begin_request(self, command: str, payload: dict) -> None:
        self.last_command = command
        self.last_payload = payload

    def finish_request(self) -> dict:
        if self.raise_timeout:
            raise TimeoutError("simulated timeout")
        return {"state": deepcopy(self.state), "events": {}, "info": {}}

    def close(self) -> None:
        self.closed = True


class CameraFakeClient(FakeClient):
    def request(self, command: str, payload: dict, **kwargs) -> dict:
        self.last_command = command
        self.last_payload = payload
        if command == "list_cameras":
            return {
                "camera_rendering_available": True,
                "cameras": [
                    {
                        "name": "front",
                        "width": 320,
                        "height": 180,
                        "vertical_fov_degrees": 70.0,
                        "near_clip": 0.03,
                        "far_clip": 50.0,
                        "robot_position": [0.0, 0.5, 0.4],
                        "robot_rotation_euler": [0.0, 0.0, 0.0],
                    }
                ],
            }
        if command == "get_camera_frame":
            return {
                "camera_frame": {
                    "name": payload["camera_name"],
                    "width": 320,
                    "height": 180,
                    "encoding": "jpeg",
                    "media_type": "image/jpeg",
                    "image_base64": base64.b64encode(b"jpeg-bytes").decode("ascii"),
                    "sequence": 1,
                    "sim_time": 0.5,
                }
            }
        return super().request(command, payload, **kwargs)


class RealtimeSampleFakeClient(FakeClient):
    def __init__(self, state: dict, *, camera_sim_time: float = 1.25) -> None:
        super().__init__(state)
        self.camera_sim_time = camera_sim_time

    def finish_request(self) -> dict:
        sampled_state = deepcopy(self.state)
        sampled_state["match"]["sim_time"] = 1.25
        applied_gamepad = np.zeros(GAMEPAD_ACTION_DIM, dtype=np.float32)
        applied_gamepad[1] = 0.8
        applied_semantic = [0.8, 0.0, 0.0, -1.0, 0.0, -1.0]
        return {
            "state": sampled_state,
            "events": {},
            "info": {
                "realtime_control_active": True,
                "realtime_control_sequence": 44,
            },
            "control": {
                "sample_id": 8,
                "unity_frame": 120,
                "sim_time": 1.25,
                "session": "driver-session",
                "sequence": 44,
                "action": applied_semantic,
                "gamepad_action": applied_gamepad.tolist(),
            },
            "camera_frames": [
                {
                    "name": "front",
                    "width": 320,
                    "height": 180,
                    "encoding": "jpeg",
                    "media_type": "image/jpeg",
                    "image_base64": base64.b64encode(b"aligned-jpeg").decode("ascii"),
                    "sequence": 9,
                    "sim_time": self.camera_sim_time,
                    "sample_id": 8,
                    "unity_frame": 120,
                    "control_sequence": 44,
                }
            ],
        }


def test_action_clipping_and_five_value_step(raw_state: dict) -> None:
    client = FakeClient(raw_state)
    env = MoSimEnv(client=client)
    try:
        env.reset(seed=7)
        transition = env.step(np.array([2, -2, 0, 5, -5, 3], dtype=np.float32))
        assert len(transition) == 5
        assert client.last_payload is not None
        np.testing.assert_array_equal(
            client.last_payload["action"], [1, -1, 0, 1, -1, 1]
        )
        assert client.last_payload["frame_skip"] == 5
    finally:
        env.close()


def test_timeout_returns_truncated_transition(raw_state: dict) -> None:
    client = FakeClient(raw_state)
    env = MoSimEnv(client=client)
    try:
        env.reset()
        client.raise_timeout = True
        _, reward, terminated, truncated, info = env.step(np.zeros(6, dtype=np.float32))
        assert reward == -25.0
        assert not terminated and truncated
        assert info["termination_reason"] == "worker_error"
        assert env._needs_restart
    finally:
        env.close()


def test_reset_restarts_failed_worker(raw_state: dict, monkeypatch) -> None:
    client = FakeClient(raw_state)
    env = MoSimEnv(client=client)
    restarted = []

    def restart() -> None:
        restarted.append(True)
        env._needs_restart = False

    monkeypatch.setattr(env, "_restart", restart)
    env._needs_restart = True
    try:
        env.reset()
        assert restarted == [True]
    finally:
        env.close()


def test_gamepad_policy_action_is_adapted_before_transport(raw_state: dict) -> None:
    client = FakeClient(raw_state)
    env = MoSimEnv(client=client, action_mode="gamepad")
    try:
        env.reset(seed=9)
        gamepad = np.zeros(GAMEPAD_ACTION_DIM, dtype=np.float32)
        gamepad[1] = 0.75
        gamepad[BUTTON_INDEX["SOUTH"]] = 1.0
        _, _, _, _, info = env.step(gamepad)
        assert client.last_payload is not None
        np.testing.assert_allclose(
            client.last_payload["action"], [0.75, 0.0, 0.0, -0.2, 0.0, -1.0]
        )
        np.testing.assert_array_equal(client.last_payload["gamepad_action"], gamepad)
        assert info["action_mode"] == "gamepad"
        assert info["gamepad_action"].shape == (25,)
        assert info["semantic_action"].shape == (6,)
    finally:
        env.close()


def test_virtual_camera_api_lists_and_captures(raw_state: dict) -> None:
    client = CameraFakeClient(raw_state)
    env = MoSimEnv(client=client)
    try:
        env.reset(seed=12)
        cameras = env.list_virtual_cameras()
        assert [camera.name for camera in cameras] == ["front"]

        frame = env.get_virtual_camera_frame("front", jpeg_quality=91)
        assert frame.image_bytes == b"jpeg-bytes"
        assert frame.sequence == 1
        assert client.last_command == "get_camera_frame"
        assert client.last_payload == {"camera_name": "front", "jpeg_quality": 91}
    finally:
        env.close()


def test_virtual_camera_api_rejects_capture_during_pending_step(raw_state: dict) -> None:
    client = CameraFakeClient(raw_state)
    env = MoSimEnv(client=client)
    try:
        env.reset()
        env.begin_step(np.zeros(6, dtype=np.float32))
        with pytest.raises(RuntimeError, match="while a step is pending"):
            env.get_virtual_camera_frame("front")
        env.finish_step()
    finally:
        env.close()


def test_realtime_sample_uses_unity_applied_action_and_aligned_camera(
    raw_state: dict,
) -> None:
    client = RealtimeSampleFakeClient(raw_state)
    env = MoSimEnv(
        client=client,
        action_mode="gamepad",
        graphical=True,
        realtime=True,
    )
    try:
        env.reset()
        requested_gamepad = np.zeros(GAMEPAD_ACTION_DIM, dtype=np.float32)
        requested_semantic = np.asarray([0.1, 0, 0, -1, 0, -1], dtype=np.float32)

        _observation, _reward, _terminated, truncated, info = (
            env.step_realtime_control(
                requested_gamepad,
                requested_semantic,
                camera_names=("front",),
                jpeg_quality=90,
            )
        )

        assert not truncated
        assert client.last_payload is not None
        assert client.last_payload["observe_only"] is True
        assert client.last_payload["camera_names"] == ["front"]
        assert client.last_payload["jpeg_quality"] == 90
        # Unity's applied 0.8 command wins over the older requested 0.1 sample.
        assert info["semantic_action"][0] == np.float32(0.8)
        assert info["gamepad_action"][1] == np.float32(0.8)
        assert info["control_sequence"] == 44
        assert info["sample_synchronized"] is True
        assert info["camera_frames"]["front"].sim_time == 1.25
    finally:
        env.close()


def test_realtime_sample_rejects_camera_from_another_simulator_time(
    raw_state: dict,
) -> None:
    client = RealtimeSampleFakeClient(raw_state, camera_sim_time=1.0)
    env = MoSimEnv(
        client=client,
        action_mode="gamepad",
        graphical=True,
        realtime=True,
    )
    try:
        env.reset()
        _, _, _, truncated, info = env.step_realtime_control(
            np.zeros(GAMEPAD_ACTION_DIM, dtype=np.float32),
            np.asarray([0, 0, 0, -1, 0, -1], dtype=np.float32),
            camera_names=("front",),
        )
        assert truncated
        assert "did not match state sim_time" in info["error"]
    finally:
        env.close()


@pytest.mark.parametrize("key", ["sample_id", "unity_frame", "control_sequence"])
def test_recording_rejects_wrong_capture_even_when_timestamp_matches(raw_state, key):
    payload = RealtimeSampleFakeClient(raw_state).finish_request()
    payload["camera_frames"][0][key] += 1
    from mosim_rl.protocol import ProtocolError

    with pytest.raises(ProtocolError, match=key):
        MoSimEnv._parse_synchronized_camera_frames(payload, ("front",), 1.25)


def test_recording_rejects_action_from_different_state_time(raw_state):
    payload = RealtimeSampleFakeClient(raw_state).finish_request()
    payload["control"]["sim_time"] = 1.5
    from mosim_rl.protocol import ProtocolError

    with pytest.raises(ProtocolError, match="timestamp"):
        MoSimEnv._parse_control_snapshot(payload)
