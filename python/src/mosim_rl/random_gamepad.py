from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from .constants import GAMEPAD_ACTION_DIM
from .gamepad import BUTTON_INDEX


@dataclass(slots=True)
class RandomGamepadActor:
    """Produces sparse, temporally coherent controller actions for smoke tests."""

    seed: int = 0
    _rng: np.random.Generator = field(init=False)
    _sticks: np.ndarray = field(default_factory=lambda: np.zeros(4, dtype=np.float32))
    _stick_ticks: int = 0
    _trigger_name: str | None = None
    _trigger_value: float = 0.0
    _trigger_ticks: int = 0

    def __post_init__(self) -> None:
        self._rng = np.random.default_rng(self.seed)

    def reset(self, seed: int | None = None) -> None:
        if seed is not None:
            self.seed = seed
        self._rng = np.random.default_rng(self.seed)
        self._sticks.fill(0.0)
        self._stick_ticks = 0
        self._trigger_name = None
        self._trigger_value = 0.0
        self._trigger_ticks = 0

    def sample(self) -> np.ndarray:
        if self._stick_ticks <= 0:
            # Moderate commands exercise driving without instantly throwing the
            # robot across the field. Right-stick Y is inactive for Reefscape.
            self._sticks[:] = [
                self._rng.uniform(-0.65, 0.65),
                self._rng.uniform(-0.65, 0.65),
                self._rng.uniform(-0.5, 0.5),
                0.0,
            ]
            self._stick_ticks = int(self._rng.integers(5, 26))
        self._stick_ticks -= 1

        action = np.zeros(GAMEPAD_ACTION_DIM, dtype=np.float32)
        action[:4] = self._sticks

        if self._trigger_ticks <= 0 and self._rng.random() < 0.035:
            self._trigger_name = str(
                self._rng.choice(["LEFT_TRIGGER", "RIGHT_TRIGGER"])
            )
            self._trigger_value = float(self._rng.uniform(0.7, 1.0))
            self._trigger_ticks = int(self._rng.integers(2, 9))
        if self._trigger_ticks > 0 and self._trigger_name is not None:
            action[BUTTON_INDEX[self._trigger_name]] = self._trigger_value
            self._trigger_ticks -= 1
            if self._trigger_ticks == 0:
                self._trigger_name = None

        # MoSim's face and D-pad bindings are edge-triggered, so pulse them for
        # exactly one policy decision just as a human taps a controller button.
        if self._rng.random() < 0.05:
            button = str(
                self._rng.choice(
                    ["SOUTH", "EAST", "WEST", "NORTH", "DPAD_DOWN", "DPAD_RIGHT"]
                )
            )
            action[BUTTON_INDEX[button]] = 1.0
        return action
