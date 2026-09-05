"""Opt-in rendered integration regression: ordered inputs and atomic video samples."""
from __future__ import annotations

import os
import socket
import threading
import time
from pathlib import Path

import numpy as np
import pytest

from mosim_rl.env import MoSimEnv
from mosim_rl.gamepad import BUTTON_INDEX, GamepadActionAdapter
from mosim_rl.realtime_gamepad import encode_realtime_control

PLAYER = Path(os.environ.get("MOSIM_GRAPHICAL_EXECUTABLE", ""))
pytestmark = [
    pytest.mark.integration,
    pytest.mark.skipif(not PLAYER.is_file(), reason="set MOSIM_GRAPHICAL_EXECUTABLE"),
]


class ScriptedStream:
    def __init__(self, env):
        self.env = env
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.lock = threading.Lock()
        self.stop = threading.Event()
        self.raw = np.zeros(25, dtype=np.float32)
        self.adapter = GamepadActionAdapter()
        self.sequence = 0
        self.sent = {}
        self.error = None
        self.thread = threading.Thread(target=self.run, daemon=True)

    def send(self, raw):
        semantic = self.adapter.to_semantic(raw)
        self.sequence += 1
        self.sent[self.sequence] = (raw.copy(), semantic.copy())
        self.sock.sendto(encode_realtime_control(
            session="recording-regression", sequence=self.sequence, active=True,
            gamepad_action=raw, semantic_action=semantic,
        ), (self.env.host, self.env.port))

    def run(self):
        try:
            deadline = time.monotonic()
            while not self.stop.is_set():
                with self.lock:
                    self.send(self.raw)
                deadline += 0.02
                self.stop.wait(max(0, deadline - time.monotonic()))
        except BaseException as exc:
            self.error = exc

    def set(self, **buttons):
        with self.lock:
            self.raw = np.zeros(25, dtype=np.float32)
            for name, value in buttons.items():
                self.raw[BUTTON_INDEX[name]] = value

    def sample(self, cameras=()):
        assert self.error is None
        with self.lock:
            raw, semantic = self.sent[self.sequence]
        observation, _, _, truncated, info = self.env.step_realtime_control(
            raw, semantic, camera_names=cameras, jpeg_quality=90,
        )
        assert not truncated, info
        with self.lock:
            sent_raw, sent_semantic = self.sent[info["control_sequence"]]
        np.testing.assert_allclose(info["gamepad_action"], sent_raw, atol=1e-6)
        np.testing.assert_allclose(info["semantic_action"], sent_semantic, atol=1e-6)
        assert info["sample_synchronized"]
        assert np.isfinite(observation).all()
        return info

    def close(self):
        self.stop.set()
        self.thread.join(2)
        assert not self.thread.is_alive()
        self.sock.sendto(encode_realtime_control(
            session="recording-regression", sequence=self.sequence + 1, active=False,
        ), (self.env.host, self.env.port))
        self.sock.close()


@pytest.mark.parametrize("brief_press", [False, True], ids=["held-rt", "queued-rt-tap"])
def test_recording_preserves_ordered_taps_and_first_place_under_camera_load(brief_press):
    env = MoSimEnv(PLAYER, action_mode="gamepad", graphical=True, realtime=True,
                   frame_skip=1, automatic_curriculum=False,
                   log_dir="runs/recording-regression")
    stream = None
    try:
        _, info = env.reset(seed=9431, options={
            "scenario": "official_match", "curriculum_stage": 4,
            "camera_mode": "field", "drive_mode": "field",
        })
        assert info["mechanism"]["has_coral"]
        cameras = tuple(c.name for c in env.list_virtual_cameras())[:3]
        assert len(cameras) == 3
        stream = ScriptedStream(env)
        stream.thread.start()
        time.sleep(0.1)

        # Two presses queued together must toggle L2 on then off. OR-merging
        # the edges leaves L2 selected instead of returning to stow.
        with stream.lock:
            neutral = np.zeros(25, dtype=np.float32)
            east = neutral.copy()
            east[BUTTON_INDEX["EAST"]] = 1
            for raw in (east, neutral, east, neutral):
                stream.send(raw)
        time.sleep(0.1)
        assert stream.sample()["mechanism"]["setpoint"] == 0

        stream.set(DPAD_RIGHT=1)
        time.sleep(0.1)
        assert stream.sample()["mechanism"]["setpoint"] == 0
        stream.set()

        stream.set(EAST=1)
        time.sleep(0.1)
        stream.set()
        time.sleep(1.0)
        assert stream.sample()["mechanism"]["setpoint"] == 3

        # Render while auto-align is held, then place during camera readback.
        stream.set(LEFT_SHOULDER=1)
        previous = None
        ages = []
        for index in range(16):
            if index == 5:
                requested_at = time.monotonic()
                if brief_press:
                    # The next neutral packet must not erase a pending RT edge.
                    with stream.lock:
                        pressed = stream.raw.copy()
                        pressed[BUTTON_INDEX["RIGHT_TRIGGER"]] = 0.9
                        stream.send(pressed)
                        stream.send(stream.raw)
                else:
                    stream.set(LEFT_SHOULDER=1, RIGHT_TRIGGER=0.9)
            info = stream.sample(cameras)
            capture = (info["sample_id"], info["unity_frame"], info["sim_time"])
            if previous is not None:
                assert all(a > b for a, b in zip(capture, previous))
            previous = capture
            for frame in info["camera_frames"].values():
                assert frame.sim_time == pytest.approx(info["sim_time"], abs=1e-4)
                assert (frame.width, frame.height) == (640, 360)
            ages.append(info["realtime_control_age_ms"])
            if index == 7:
                assert not info["mechanism"]["has_coral"], "first held RT was lost"
                print("release confirmed by camera sample (polling bound {:.0f} ms)".format(
                    (time.monotonic() - requested_at) * 1000))
                stream.set()
        print("16 synchronized batches; control age mean={:.1f} max={:.1f} ms".format(
            np.mean(ages), max(ages)))
    finally:
        if stream is not None:
            stream.close()
        env.close()
