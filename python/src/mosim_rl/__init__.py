from __future__ import annotations

from gymnasium.envs.registration import register, registry

from .constants import ACTION_DIM, GAMEPAD_ACTION_DIM, OBSERVATION_DIM, PROTOCOL_VERSION
from .env import MoSimEnv
from .vec_env import MoSimVecEnv

ENV_ID = "MoSim-Reefscape-Coral-v0"
GAMEPAD_ENV_ID = "MoSim-Reefscape-Gamepad-v0"

if ENV_ID not in registry:
    # PhysX contact solving is repeatable within a tight floating-point tolerance,
    # but is not bit-for-bit deterministic. This flag makes Gymnasium skip its
    # inappropriate exact-equality transition check while retaining all API checks.
    register(
        id=ENV_ID,
        entry_point="mosim_rl.env:MoSimEnv",
        nondeterministic=True,
    )

if GAMEPAD_ENV_ID not in registry:
    register(
        id=GAMEPAD_ENV_ID,
        entry_point="mosim_rl.env:MoSimEnv",
        kwargs={"action_mode": "gamepad"},
        nondeterministic=True,
    )

__all__ = [
    "ACTION_DIM",
    "ENV_ID",
    "GAMEPAD_ACTION_DIM",
    "GAMEPAD_ENV_ID",
    "MoSimEnv",
    "MoSimVecEnv",
    "OBSERVATION_DIM",
    "PROTOCOL_VERSION",
]
