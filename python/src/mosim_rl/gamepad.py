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


@dataclass(slots=True)
class GamepadActionAdapter:
    """Converts NitroGen's flat gamepad output to the six-value Unity command."""

    button_threshold: float = 0.5
    target_setpoint: int = 0
    station_mode: bool = False
    previous_pressed: np.ndarray = field(
        default_factory=lambda: np.zeros(len(NITROGEN_BUTTONS), dtype=bool)
    )

    def reset(self) -> None:
        self.target_setpoint = 0
        self.station_mode = False
        self.previous_pressed.fill(False)

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
        if rose("DPAD_DOWN"):
            self.target_setpoint = 0
        elif rose("SOUTH"):
            self.target_setpoint = 2
        elif rose("EAST"):
            self.target_setpoint = 3
        elif rose("WEST"):
            self.target_setpoint = 4
        elif rose("NORTH"):
            self.target_setpoint = 5

        if rose("DPAD_RIGHT"):
            self.station_mode = not self.station_mode

        left_trigger = float(gamepad[BUTTON_INDEX["LEFT_TRIGGER"]])
        right_trigger = float(gamepad[BUTTON_INDEX["RIGHT_TRIGGER"]])
        if held("LEFT_TRIGGER"):
            self.target_setpoint = 1
            manipulator = left_trigger
        elif intake_was_held:
            self.target_setpoint = 0
            manipulator = 0.0
        elif held("RIGHT_TRIGGER"):
            manipulator = -right_trigger
        else:
            manipulator = 0.0

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
