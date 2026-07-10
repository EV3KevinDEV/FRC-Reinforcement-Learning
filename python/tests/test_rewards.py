from __future__ import annotations

from copy import deepcopy

import numpy as np

from mosim_rl.rewards import RewardCalculator


def test_reward_terms_for_successful_coral_cycle(raw_state: dict) -> None:
    calculator = RewardCalculator()
    calculator.reset(raw_state)
    next_state = deepcopy(raw_state)
    next_state["task"]["active_distance"] = 2.0
    next_state["task"]["heading_error"] = 0.25
    next_state["mechanism"]["has_coral"] = True
    next_state["match"]["score"]["coral_points"] = 5
    next_state["match"]["score"]["total_points"] = 5
    action = np.array([1.0, 0.0, 0.0, 0.0, 1.0, 0.0], dtype=np.float32)

    result = calculator.calculate(next_state, action, {"cycle_success": True})

    assert result.terms["official_score"] == 25.0
    assert result.terms["pickup"] == 5.0
    assert result.terms["cycle_success"] == 10.0
    assert result.terms["distance_progress"] == 2.0
    assert result.terms["heading_progress"] == 0.125
    assert not result.terminated


def test_drop_and_terminal_penalties(raw_state: dict) -> None:
    raw_state["mechanism"]["has_coral"] = True
    calculator = RewardCalculator()
    calculator.reset(raw_state)
    next_state = deepcopy(raw_state)
    next_state["mechanism"]["has_coral"] = False
    next_state["robot"]["up"] = [1.0, 0.1, 0.0]

    result = calculator.calculate(next_state, np.zeros(6, dtype=np.float32))

    assert result.terms["drop"] == -5.0
    assert result.terms["terminal_penalty"] == -25.0
    assert result.terminated and result.reason == "tipped"


def test_match_completion_is_natural_termination(raw_state: dict) -> None:
    calculator = RewardCalculator()
    calculator.reset(raw_state)
    raw_state["match"]["match_complete"] = True
    result = calculator.calculate(raw_state, np.zeros(6, dtype=np.float32))
    assert result.terminated and result.reason == "match_complete"
