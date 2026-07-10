from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np


@dataclass(slots=True)
class RewardResult:
    total: float
    terms: dict[str, float]
    terminated: bool
    reason: str | None


@dataclass(slots=True)
class RewardCalculator:
    previous_distance: float | None = None
    previous_heading_error: float | None = None
    previous_has_coral: bool = False
    previous_relevant_score: float = 0.0
    previous_action: np.ndarray = field(
        default_factory=lambda: np.zeros(6, dtype=np.float32)
    )

    def reset(self, raw_state: dict[str, Any]) -> None:
        task = raw_state.get("task", {})
        mechanism = raw_state.get("mechanism", {})
        self.previous_distance = float(
            task.get("active_distance", task.get("target_distance", 0.0))
        )
        self.previous_heading_error = abs(float(task.get("heading_error", 0.0)))
        self.previous_has_coral = bool(mechanism.get("has_coral", False))
        self.previous_relevant_score = self._relevant_score(raw_state)
        self.previous_action = np.zeros(6, dtype=np.float32)

    @staticmethod
    def _relevant_score(raw_state: dict[str, Any]) -> float:
        score = raw_state.get("match", {}).get("score", {})
        return float(
            score.get("coral_points", 0.0)
            + score.get("trough_points", 0.0)
            + score.get("leave_points", 0.0)
        )

    def calculate(
        self,
        raw_state: dict[str, Any],
        action: np.ndarray,
        events: dict[str, Any] | None = None,
    ) -> RewardResult:
        events = events or {}
        task = raw_state.get("task", {})
        robot = raw_state.get("robot", {})
        mechanism = raw_state.get("mechanism", {})
        match = raw_state.get("match", {})
        terms: dict[str, float] = {}

        relevant_score = self._relevant_score(raw_state)
        terms["official_score"] = 5.0 * (relevant_score - self.previous_relevant_score)

        has_coral = bool(mechanism.get("has_coral", False))
        terms["pickup"] = 5.0 if has_coral and not self.previous_has_coral else 0.0
        terms["drop"] = (
            -5.0
            if self.previous_has_coral
            and not has_coral
            and not events.get("cycle_success", False)
            else 0.0
        )
        terms["cycle_success"] = 10.0 if events.get("cycle_success", False) else 0.0

        distance = float(task.get("active_distance", task.get("target_distance", 0.0)))
        terms["distance_progress"] = (
            2.0 * float(np.clip(self.previous_distance - distance, -1.0, 1.0))
            if self.previous_distance is not None
            else 0.0
        )
        heading_error = abs(float(task.get("heading_error", 0.0)))
        terms["heading_progress"] = (
            0.5
            * float(np.clip(self.previous_heading_error - heading_error, -np.pi, np.pi))
            if has_coral and self.previous_heading_error is not None
            else 0.0
        )
        terms["drive_effort"] = -0.002 * float(np.dot(action[:3], action[:3]))
        delta_action = action - self.previous_action
        terms["action_rate"] = -0.01 * float(np.dot(delta_action, delta_action))

        position = robot.get("position", [0.0, 0.0, 0.0])
        up = robot.get("up", [0.0, 1.0, 0.0])
        tipped = len(up) >= 2 and float(up[1]) < 0.5
        out_of_bounds = len(position) >= 3 and (
            abs(float(position[0])) > 10.0 or abs(float(position[2])) > 6.0
        )
        terminated = False
        reason: str | None = None
        if tipped:
            terms["terminal_penalty"] = -25.0
            terminated, reason = True, "tipped"
        elif out_of_bounds:
            terms["terminal_penalty"] = -25.0
            terminated, reason = True, "out_of_bounds"
        elif events.get("match_complete", False) or match.get("match_complete", False):
            terms["terminal_penalty"] = 0.0
            terminated, reason = True, "match_complete"
        else:
            terms["terminal_penalty"] = 0.0

        self.previous_distance = distance
        self.previous_heading_error = heading_error
        self.previous_has_coral = has_coral
        self.previous_relevant_score = relevant_score
        self.previous_action = action.copy()
        total = float(sum(terms.values()))
        return RewardResult(total, terms, terminated, reason)
