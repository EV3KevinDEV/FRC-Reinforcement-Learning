from __future__ import annotations

import json

import numpy as np
import pytest

from mosim_rl.constants import GAMEPAD_ACTION_DIM, PROTOCOL_VERSION
from mosim_rl.realtime_gamepad import encode_realtime_control


def test_realtime_control_datagram_contains_both_action_representations() -> None:
    gamepad = np.linspace(0.0, 1.0, GAMEPAD_ACTION_DIM, dtype=np.float32)
    semantic = np.linspace(-1.0, 1.0, 6, dtype=np.float32)

    payload = json.loads(
        encode_realtime_control(
            session="driver-session",
            sequence=17,
            active=True,
            gamepad_action=gamepad,
            semantic_action=semantic,
        ).decode("utf-8")
    )

    assert payload["v"] == PROTOCOL_VERSION
    assert payload["session"] == "driver-session"
    assert payload["sequence"] == 17
    assert payload["active"] is True
    np.testing.assert_allclose(payload["gamepad_action"], gamepad)
    np.testing.assert_allclose(payload["action"], semantic)


def test_realtime_control_stop_packet_needs_no_action_arrays() -> None:
    payload = json.loads(
        encode_realtime_control(
            session="driver-session",
            sequence=18,
            active=False,
        ).decode("utf-8")
    )

    assert payload["active"] is False
    assert "action" not in payload
    assert "gamepad_action" not in payload


def test_realtime_control_rejects_misaligned_shapes() -> None:
    with pytest.raises(ValueError, match="gamepad action shape"):
        encode_realtime_control(
            session="driver-session",
            sequence=1,
            active=True,
            gamepad_action=np.zeros(24, dtype=np.float32),
            semantic_action=np.zeros(6, dtype=np.float32),
        )
