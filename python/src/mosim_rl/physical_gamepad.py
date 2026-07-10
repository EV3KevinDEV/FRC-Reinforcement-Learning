from __future__ import annotations

from dataclasses import dataclass

import numpy as np
import pygame
from pygame._sdl2 import controller

from .constants import GAMEPAD_ACTION_DIM, NITROGEN_BUTTONS
from .gamepad import BUTTON_INDEX


@dataclass(slots=True)
class PhysicalGamepad:
    """Reads a standardized SDL controller into NitroGen's 25-value layout."""

    index: int = 0
    deadzone: float = 0.12
    _controller: controller.Controller | None = None

    def open(self) -> "PhysicalGamepad":
        pygame.init()
        controller.init()
        count = controller.get_count()
        if not 0 <= self.index < count:
            self.close()
            raise RuntimeError(
                f"controller {self.index} is unavailable; SDL detected {count} controller(s)"
            )
        self._controller = controller.Controller(self.index)
        return self

    @property
    def name(self) -> str:
        return self._require_controller().name

    def _require_controller(self) -> controller.Controller:
        if self._controller is None:
            raise RuntimeError("physical gamepad is not open")
        if not self._controller.attached():
            raise RuntimeError("physical gamepad was disconnected")
        return self._controller

    def _stick_axis(self, axis: int, *, invert: bool = False) -> float:
        raw = float(self._require_controller().get_axis(axis))
        value = float(np.clip(raw / 32767.0, -1.0, 1.0))
        if invert:
            value = -value
        magnitude = abs(value)
        if magnitude <= self.deadzone:
            return 0.0
        return float(
            np.sign(value) * (magnitude - self.deadzone) / (1.0 - self.deadzone)
        )

    def _trigger_axis(self, axis: int) -> float:
        raw = float(self._require_controller().get_axis(axis))
        return float(np.clip(raw / 32767.0, 0.0, 1.0))

    def _button(self, button: int) -> float:
        return float(bool(self._require_controller().get_button(button)))

    def read(self) -> np.ndarray:
        pygame.event.pump()
        action = np.zeros(GAMEPAD_ACTION_DIM, dtype=np.float32)
        action[0] = self._stick_axis(pygame.CONTROLLER_AXIS_LEFTX)
        action[1] = self._stick_axis(pygame.CONTROLLER_AXIS_LEFTY, invert=True)
        action[2] = self._stick_axis(pygame.CONTROLLER_AXIS_RIGHTX)
        action[3] = self._stick_axis(pygame.CONTROLLER_AXIS_RIGHTY, invert=True)

        button_map = {
            "BACK": pygame.CONTROLLER_BUTTON_BACK,
            "DPAD_DOWN": pygame.CONTROLLER_BUTTON_DPAD_DOWN,
            "DPAD_LEFT": pygame.CONTROLLER_BUTTON_DPAD_LEFT,
            "DPAD_RIGHT": pygame.CONTROLLER_BUTTON_DPAD_RIGHT,
            "DPAD_UP": pygame.CONTROLLER_BUTTON_DPAD_UP,
            "EAST": pygame.CONTROLLER_BUTTON_B,
            "GUIDE": pygame.CONTROLLER_BUTTON_GUIDE,
            "LEFT_SHOULDER": pygame.CONTROLLER_BUTTON_LEFTSHOULDER,
            "LEFT_THUMB": pygame.CONTROLLER_BUTTON_LEFTSTICK,
            "NORTH": pygame.CONTROLLER_BUTTON_Y,
            "RIGHT_SHOULDER": pygame.CONTROLLER_BUTTON_RIGHTSHOULDER,
            "RIGHT_THUMB": pygame.CONTROLLER_BUTTON_RIGHTSTICK,
            "SOUTH": pygame.CONTROLLER_BUTTON_A,
            "START": pygame.CONTROLLER_BUTTON_START,
            "WEST": pygame.CONTROLLER_BUTTON_X,
        }
        for name, button in button_map.items():
            action[BUTTON_INDEX[name]] = self._button(button)

        action[BUTTON_INDEX["LEFT_TRIGGER"]] = self._trigger_axis(
            pygame.CONTROLLER_AXIS_TRIGGERLEFT
        )
        action[BUTTON_INDEX["RIGHT_TRIGGER"]] = self._trigger_axis(
            pygame.CONTROLLER_AXIS_TRIGGERRIGHT
        )
        right_x, right_y = float(action[2]), float(action[3])
        action[BUTTON_INDEX["RIGHT_LEFT"]] = max(-right_x, 0.0)
        action[BUTTON_INDEX["RIGHT_RIGHT"]] = max(right_x, 0.0)
        action[BUTTON_INDEX["RIGHT_UP"]] = max(right_y, 0.0)
        action[BUTTON_INDEX["RIGHT_BOTTOM"]] = max(-right_y, 0.0)
        return action

    def close(self) -> None:
        if self._controller is not None:
            self._controller.quit()
            self._controller = None
        if controller.get_init():
            controller.quit()
        pygame.quit()

    def __enter__(self) -> "PhysicalGamepad":
        return self.open()

    def __exit__(self, *_args: object) -> None:
        self.close()


def active_button_names(action: np.ndarray, threshold: float = 0.5) -> list[str]:
    return [
        name
        for name in NITROGEN_BUTTONS
        if float(action[BUTTON_INDEX[name]]) > threshold
    ]
