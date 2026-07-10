from __future__ import annotations

from copy import deepcopy

import pytest


@pytest.fixture
def raw_state() -> dict:
    state = {
        "robot": {
            "position": [1.0, 0.2, -2.0],
            "yaw_degrees": 90.0,
            "local_velocity": [3.5, 0.0, -1.75],
            "yaw_rate": 2.0,
            "up": [0.0, 1.0, 0.0],
            "grounded": True,
            "enabled": True,
        },
        "mechanism": {
            "setpoint": 2,
            "arm_angle": 45.0,
            "elevator_height": 20.0,
            "intake_angle": -45.0,
            "algae_arms_angle": 0.0,
            "has_coral": False,
            "coral_state": 0,
            "station_mode": False,
        },
        "task": {
            "phase": "seek",
            "coral_relative": [2.0, -1.0],
            "coral_distance": 3.0,
            "coral_velocity": [0.5, -0.5],
            "coral_valid": True,
            "target_relative": [4.0, 2.0],
            "target_distance": 5.0,
            "active_distance": 3.0,
            "heading_error": 0.5,
            "target_level": 3,
        },
        "match": {
            "time_remaining": 150.0,
            "game_state": "Auto",
            "sim_time": 0.0,
            "score": {
                "coral_points": 0,
                "trough_points": 0,
                "net_points": 0,
                "processor_points": 0,
                "climb_points": 0,
                "park_points": 0,
                "leave_points": 0,
                "coral_scored": 0,
                "algae_scored": 0,
                "total_points": 0,
            },
            "score_delta": 0,
            "match_complete": False,
        },
    }
    return deepcopy(state)
