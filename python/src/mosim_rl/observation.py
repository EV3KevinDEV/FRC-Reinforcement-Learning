from __future__ import annotations

from collections.abc import Mapping, Sequence
from typing import Any

import numpy as np

from .constants import GAME_STATES, OBSERVATION_DIM, SCORE_KEYS, TASK_PHASES


def _clip(value: float, scale: float = 1.0) -> float:
    return float(np.clip(float(value) / scale, -1.0, 1.0))


def _vector(value: Any, length: int) -> list[float]:
    if not isinstance(value, Sequence) or isinstance(value, (str, bytes)):
        return [0.0] * length
    result = [float(item) for item in value[:length]]
    return result + [0.0] * (length - len(result))


def _one_hot(value: str, choices: Sequence[str]) -> list[float]:
    return [1.0 if value == choice else 0.0 for choice in choices]


class ObservationEncoder:
    """Converts raw Unity telemetry into the stable 62-element policy vector."""

    score_scales: Mapping[str, float] = {
        "coral_points": 200.0,
        "trough_points": 100.0,
        "net_points": 100.0,
        "processor_points": 100.0,
        "climb_points": 12.0,
        "park_points": 2.0,
        "leave_points": 3.0,
        "coral_scored": 48.0,
        "algae_scored": 18.0,
        "total_points": 300.0,
    }

    def encode(
        self, raw_state: Mapping[str, Any], previous_action: np.ndarray
    ) -> np.ndarray:
        robot = raw_state.get("robot", {})
        mechanism = raw_state.get("mechanism", {})
        task = raw_state.get("task", {})
        match = raw_state.get("match", {})
        score = match.get("score", {})

        position = _vector(robot.get("position"), 3)
        velocity = _vector(robot.get("local_velocity"), 3)
        up = _vector(robot.get("up"), 3)
        yaw = np.deg2rad(float(robot.get("yaw_degrees", 0.0)))

        values: list[float] = [
            _clip(position[0], 9.0),
            _clip(position[2], 5.0),
            float(np.sin(yaw)),
            float(np.cos(yaw)),
            _clip(velocity[0], 7.0),
            _clip(velocity[2], 7.0),
            _clip(robot.get("yaw_rate", 0.0), 8.0),
            _clip(up[0]),
            _clip(up[1]),
            _clip(up[2]),
            1.0 if robot.get("grounded", False) else 0.0,
            1.0 if robot.get("enabled", False) else 0.0,
            _clip(float(mechanism.get("setpoint", 0.0)) - 2.5, 2.5),
            _clip(mechanism.get("arm_angle", 0.0), 180.0),
            _clip(mechanism.get("elevator_height", 0.0), 80.0),
            _clip(mechanism.get("intake_angle", 0.0), 180.0),
            _clip(mechanism.get("algae_arms_angle", 0.0), 180.0),
            1.0 if mechanism.get("has_coral", False) else 0.0,
            _clip(mechanism.get("coral_state", 0.0), 8.0),
            1.0 if mechanism.get("station_mode", False) else 0.0,
        ]

        values.extend(_one_hot(str(task.get("phase", "seek")), TASK_PHASES))
        coral_relative = _vector(task.get("coral_relative"), 2)
        coral_velocity = _vector(task.get("coral_velocity"), 2)
        values.extend(
            [
                _clip(coral_relative[0], 18.0),
                _clip(coral_relative[1], 18.0),
                _clip(task.get("coral_distance", 0.0), 20.0),
                _clip(coral_velocity[0], 7.0),
                _clip(coral_velocity[1], 7.0),
                1.0 if task.get("coral_valid", False) else 0.0,
            ]
        )
        target_relative = _vector(task.get("target_relative"), 2)
        heading_error = float(task.get("heading_error", 0.0))
        values.extend(
            [
                _clip(target_relative[0], 18.0),
                _clip(target_relative[1], 18.0),
                _clip(task.get("target_distance", 0.0), 20.0),
                float(np.sin(heading_error)),
                float(np.cos(heading_error)),
            ]
        )
        target_level = int(np.clip(task.get("target_level", 1), 1, 4))
        values.extend([1.0 if target_level == level else 0.0 for level in range(1, 5)])

        values.append(_clip(match.get("time_remaining", 0.0), 150.0))
        values.extend(_one_hot(str(match.get("game_state", "Auto")), GAME_STATES))
        for key in SCORE_KEYS:
            values.append(_clip(score.get(key, 0.0), self.score_scales[key]))
        values.append(_clip(match.get("score_delta", 0.0), 12.0))
        values.extend(float(value) for value in np.clip(previous_action, -1.0, 1.0))

        observation = np.asarray(values, dtype=np.float32)
        if observation.shape != (OBSERVATION_DIM,):
            raise ValueError(
                f"observation schema produced {observation.shape}, expected {(OBSERVATION_DIM,)}"
            )
        if not np.isfinite(observation).all():
            raise ValueError("observation contains NaN or infinity")
        return observation
