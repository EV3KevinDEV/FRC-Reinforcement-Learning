from __future__ import annotations

import numpy as np

from mosim_rl.constants import (
    GAMEPAD_ACTION_DIM,
    GAMEPAD_ACTIVE_MASK,
    NITROGEN_BUTTONS,
)
from mosim_rl.gamepad import BUTTON_INDEX, GamepadActionAdapter
from mosim_rl.gamepad_rollout import scripted_gamepad_action, scripted_pickup_action
from mosim_rl.random_gamepad import RandomGamepadActor


def action(**values: float) -> np.ndarray:
    result = np.zeros(GAMEPAD_ACTION_DIM, dtype=np.float32)
    axes = {"LEFT_X": 0, "LEFT_Y": 1, "RIGHT_X": 2, "RIGHT_Y": 3}
    for name, value in values.items():
        result[axes.get(name, BUTTON_INDEX.get(name, -1))] = value
    return result


def test_nitrogen_layout_and_drive_axis_mapping() -> None:
    assert len(NITROGEN_BUTTONS) == 21
    assert GAMEPAD_ACTIVE_MASK.shape == (25,)
    adapter = GamepadActionAdapter()
    semantic = adapter.to_semantic(action(LEFT_X=0.25, LEFT_Y=0.75, RIGHT_X=-0.5))
    np.testing.assert_allclose(semantic, [0.75, -0.25, 0.5, -1.0, 0.0, -1.0])


def test_complete_physical_controller_buttons_are_active() -> None:
    for name in (
        "BACK",
        "DPAD_DOWN",
        "DPAD_LEFT",
        "DPAD_RIGHT",
        "DPAD_UP",
        "LEFT_SHOULDER",
        "LEFT_THUMB",
        "RIGHT_SHOULDER",
        "RIGHT_THUMB",
        "START",
    ):
        assert GAMEPAD_ACTIVE_MASK[BUTTON_INDEX[name]]


def test_face_buttons_select_persistent_scoring_levels() -> None:
    adapter = GamepadActionAdapter()
    expected = {"SOUTH": -0.2, "EAST": 0.2, "WEST": 0.6, "NORTH": 1.0}
    for button, target in expected.items():
        selected = adapter.to_semantic(action(**{button: 1.0}))
        released = adapter.to_semantic(action())
        assert selected[3] == np.float32(target)
        assert released[3] == np.float32(target)


def test_overlapping_target_buttons_use_last_press_and_held_fallback() -> None:
    adapter = GamepadActionAdapter()

    # Simultaneous A+B retains the historical A priority.
    simultaneous = adapter.to_semantic(action(SOUTH=1.0, EAST=1.0))
    assert simultaneous[3] == np.float32(-0.2)

    # Releasing A promotes the still-held B instead of losing B's command.
    fallback = adapter.to_semantic(action(EAST=1.0))
    assert fallback[3] == np.float32(0.2)

    # A newly pressed X takes priority over the older held B.
    newest = adapter.to_semantic(action(EAST=1.0, WEST=1.0))
    assert newest[3] == np.float32(0.6)


def test_right_trigger_takes_over_immediately_when_left_trigger_releases() -> None:
    adapter = GamepadActionAdapter()

    both = adapter.to_semantic(action(LEFT_TRIGGER=0.8, RIGHT_TRIGGER=0.9))
    assert both[4] == np.float32(0.8)

    score = adapter.to_semantic(action(RIGHT_TRIGGER=0.9))
    assert score[4] == np.float32(-0.9)


def test_triggers_and_station_toggle_match_mosim_gamepad_behavior() -> None:
    adapter = GamepadActionAdapter()
    intake = adapter.to_semantic(action(LEFT_TRIGGER=0.8))
    assert intake[3] == np.float32(-0.6)
    assert intake[4] == np.float32(0.8)
    released = adapter.to_semantic(action())
    assert released[3] == np.float32(-1.0)
    assert released[4] == 0.0

    place = adapter.to_semantic(action(RIGHT_TRIGGER=0.9))
    assert place[4] == np.float32(-0.9)

    station = adapter.to_semantic(action(DPAD_RIGHT=1.0))
    held = adapter.to_semantic(action(DPAD_RIGHT=1.0))
    adapter.to_semantic(action())
    ground = adapter.to_semantic(action(DPAD_RIGHT=1.0))
    assert station[5] == held[5] == 1.0
    assert ground[5] == -1.0


def test_left_trigger_preserves_selected_algae_pickup_position() -> None:
    adapter = GamepadActionAdapter()

    adapter.to_semantic(action(DPAD_UP=1.0))
    adapter.to_semantic(action())
    stack_algae = adapter.to_semantic(action(SOUTH=1.0))
    adapter.to_semantic(action())
    stack_intake = adapter.to_semantic(action(LEFT_TRIGGER=0.9))
    assert stack_algae[3] == stack_intake[3] == np.float32(-0.2)
    assert stack_intake[4] == np.float32(0.9)

    adapter.reset()
    low_algae = adapter.to_semantic(action(EAST=1.0))
    adapter.to_semantic(action())
    low_intake = adapter.to_semantic(action(LEFT_TRIGGER=0.8))
    low_released = adapter.to_semantic(action())
    assert low_algae[3] == low_intake[3] == low_released[3] == np.float32(0.2)
    assert low_intake[4] == np.float32(0.8)

    adapter.reset()
    high_algae = adapter.to_semantic(action(WEST=1.0))
    adapter.to_semantic(action())
    high_intake = adapter.to_semantic(action(LEFT_TRIGGER=0.7))
    assert high_algae[3] == high_intake[3] == np.float32(0.6)
    assert high_intake[4] == np.float32(0.7)


def test_random_gamepad_actor_is_sparse_coherent_and_seeded() -> None:
    first = RandomGamepadActor(seed=31)
    second = RandomGamepadActor(seed=31)
    actions_a = np.stack([first.sample() for _ in range(200)])
    actions_b = np.stack([second.sample() for _ in range(200)])
    np.testing.assert_array_equal(actions_a, actions_b)
    assert np.all(actions_a[:, :4] >= -1.0)
    assert np.all(actions_a[:, :4] <= 1.0)
    assert np.all(actions_a[:, 4:] >= 0.0)
    assert np.all(actions_a[:, 4:] <= 1.0)
    assert np.max(np.count_nonzero(actions_a[:, 4:] > 0.5, axis=1)) <= 2
    stick_changes = np.count_nonzero(np.any(np.diff(actions_a[:, :4], axis=0), axis=1))
    assert 5 <= stick_changes <= 40
    assert np.count_nonzero(actions_a[:, 4:] > 0.5) > 0


def test_scripted_gamepad_demo_exercises_every_active_control_group() -> None:
    actions = np.stack([scripted_gamepad_action(step) for step in range(180)])
    assert actions[0, 1] == np.float32(0.6)
    assert actions[20, 0] == np.float32(0.6)
    assert actions[40, 2] == np.float32(0.5)
    for step, button in {
        60: "SOUTH",
        70: "EAST",
        80: "WEST",
        90: "NORTH",
        100: "LEFT_TRIGGER",
        130: "RIGHT_TRIGGER",
        145: "DPAD_RIGHT",
        160: "DPAD_DOWN",
    }.items():
        assert actions[step, BUTTON_INDEX[button]] > 0.5


def test_scripted_pickup_holds_only_the_normal_intake_binding() -> None:
    initial = scripted_pickup_action(0)
    active = scripted_pickup_action(30)
    released = scripted_pickup_action(130)
    assert np.count_nonzero(initial) == 0
    assert active[BUTTON_INDEX["LEFT_TRIGGER"]] == np.float32(0.9)
    assert np.count_nonzero(active) == 1
    assert np.count_nonzero(released) == 0
