from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from .constants import (
    GAMEPAD_ACTION_DIM,
    GAMEPAD_ACTION_HIGH,
    GAMEPAD_ACTION_LOW,
    NITROGEN_BUTTONS,
)

BUTTON_OFFSET = 4
BUTTON_INDEX = {
    name: BUTTON_OFFSET + index for index, name in enumerate(NITROGEN_BUTTONS)
}
TARGET_BUTTON_SETPOINTS = {
    "DPAD_DOWN": 0,
    "SOUTH": 2,
    "EAST": 3,
    "WEST": 4,
    "NORTH": 5,
}


@dataclass(slots=True)
class GamepadActionAdapter:
    """Converts NitroGen's flat gamepad output to the six-value Unity command."""

    button_threshold: float = 0.5
    target_setpoint: int = 0
    station_mode: bool = False
    previous_pressed: np.ndarray = field(
        default_factory=lambda: np.zeros(len(NITROGEN_BUTTONS), dtype=bool)
    )
    target_button_stack: list[str] = field(default_factory=list)

    def reset(self) -> None:
        self.target_setpoint = 0
        self.station_mode = False
        self.previous_pressed.fill(False)
        self.target_button_stack.clear()

    @staticmethod
    def _target_value(target: int) -> float:
        return float(target / 2.5 - 1.0)

    def to_semantic(self, action: np.ndarray) -> np.ndarray:
        gamepad = np.asarray(action, dtype=np.float32)
        if gamepad.shape != (GAMEPAD_ACTION_DIM,):
            raise ValueError(
                f"expected gamepad action shape {(GAMEPAD_ACTION_DIM,)}, got {gamepad.shape}"
            )
        gamepad = np.clip(gamepad, GAMEPAD_ACTION_LOW, GAMEPAD_ACTION_HIGH)
        pressed = gamepad[BUTTON_OFFSET:] > self.button_threshold
        rising = pressed & ~self.previous_pressed

        def rose(name: str) -> bool:
            return bool(rising[BUTTON_INDEX[name] - BUTTON_OFFSET])

        def held(name: str) -> bool:
            return bool(pressed[BUTTON_INDEX[name] - BUTTON_OFFSET])

        intake_was_held = bool(
            self.previous_pressed[BUTTON_INDEX["LEFT_TRIGGER"] - BUTTON_OFFSET]
        )
        # Track target buttons by press order. The newest press wins, and
        # releasing it immediately falls back to another target that remains
        # held. This avoids losing commands during overlapping button presses.
        self.target_button_stack = [
            name for name in self.target_button_stack if held(name)
        ]
        for name in reversed(tuple(TARGET_BUTTON_SETPOINTS)):
            if rose(name):
                if name in self.target_button_stack:
                    self.target_button_stack.remove(name)
                self.target_button_stack.append(name)
        if self.target_button_stack:
            self.target_setpoint = TARGET_BUTTON_SETPOINTS[
                self.target_button_stack[-1]
            ]

        if rose("DPAD_RIGHT"):
            self.station_mode = not self.station_mode

        left_trigger = float(gamepad[BUTTON_INDEX["LEFT_TRIGGER"]])
        right_trigger = float(gamepad[BUTTON_INDEX["RIGHT_TRIGGER"]])
        if held("LEFT_TRIGGER"):
            # Team 118 uses LT as the algae roller while B/X (or A for
            # stacked algae) holds the pickup geometry. Do not collapse those
            # selected positions into the ground-intake setpoint.
            if self.target_setpoint not in (2, 3, 4):
                self.target_setpoint = 1
            manipulator = left_trigger
        else:
            if intake_was_held and self.target_setpoint == 1:
                self.target_setpoint = 0
            # If both triggers were held, scoring takes over immediately when
            # LT is released instead of inserting a blank control frame.
            manipulator = -right_trigger if held("RIGHT_TRIGGER") else 0.0

        semantic = np.asarray(
            [
                gamepad[1],  # left-stick Y -> forward
                -gamepad[0],  # left-stick X -> left strafe
                -gamepad[2],  # MoSim's right-stick X binding is inverted
                self._target_value(self.target_setpoint),
                manipulator,
                1.0 if self.station_mode else -1.0,
            ],
            dtype=np.float32,
        )
        self.previous_pressed = pressed.copy()
        return semantic
