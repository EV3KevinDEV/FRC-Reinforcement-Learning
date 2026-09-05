from __future__ import annotations

import numpy as np

PROTOCOL_VERSION = 1
MAX_FRAME_BYTES = 1 << 20
ACTION_DIM = 6
GAMEPAD_ACTION_DIM = 25
OBSERVATION_DIM = 62
DEFAULT_HOST = "127.0.0.1"
DEFAULT_NUM_ENVS = 8
# MoSimulator's authored PhysX timestep is approximately 4.5 ms. Protocol
# frame-skip units remain 20 ms control quanta so five gives a 10 Hz policy.
DEFAULT_FIXED_DT = 0.0045
DEFAULT_CONTROL_DT = 0.02
DEFAULT_FRAME_SKIP = 5
DEFAULT_STEP_TIMEOUT = 10.0
DEFAULT_CONNECT_TIMEOUT = 60.0

ACTION_LOW = np.full(ACTION_DIM, -1.0, dtype=np.float32)
ACTION_HIGH = np.full(ACTION_DIM, 1.0, dtype=np.float32)

# NitroGen's current action layout: [left stick xy, right stick xy, 21 buttons].
NITROGEN_BUTTONS = (
    "BACK",
    "DPAD_DOWN",
    "DPAD_LEFT",
    "DPAD_RIGHT",
    "DPAD_UP",
    "EAST",
    "GUIDE",
    "LEFT_SHOULDER",
    "LEFT_THUMB",
    "LEFT_TRIGGER",
    "NORTH",
    "RIGHT_BOTTOM",
    "RIGHT_LEFT",
    "RIGHT_RIGHT",
    "RIGHT_SHOULDER",
    "RIGHT_THUMB",
    "RIGHT_TRIGGER",
    "RIGHT_UP",
    "SOUTH",
    "START",
    "WEST",
)
GAMEPAD_ACTION_LOW = np.concatenate(
    (np.full(4, -1.0, dtype=np.float32), np.zeros(21, dtype=np.float32))
)
GAMEPAD_ACTION_HIGH = np.ones(GAMEPAD_ACTION_DIM, dtype=np.float32)

# Controls used by the Team 118 adapter. Keeping inactive synthetic buttons in
# the fixed layout makes actions directly compatible with NitroGen outputs.
GAMEPAD_ACTIVE_MASK = np.asarray(
    [
        True,  # left x
        True,  # left y
        True,  # right x
        False,  # right y
        True,  # BACK: exit physical-controller example
        True,  # DPAD_DOWN: stow
        True,  # DPAD_LEFT: toggle normal/L1 intake mode
        False,  # DPAD_RIGHT: reserved, intentionally unbound
        True,  # DPAD_UP: toggle coral/algae mode
        True,  # EAST: L2
        False,  # GUIDE
        True,  # LEFT_SHOULDER: auto-align left
        False,  # LEFT_THUMB: reserved, climber intentionally unbound
        True,  # LEFT_TRIGGER: intake
        True,  # NORTH: L4
        False,  # RIGHT_BOTTOM
        False,  # RIGHT_LEFT
        False,  # RIGHT_RIGHT
        True,  # RIGHT_SHOULDER: auto-align right
        True,  # RIGHT_THUMB: flip robot camera
        True,  # RIGHT_TRIGGER: place
        False,  # RIGHT_UP
        True,  # SOUTH: L1
        True,  # START: reset physical-controller episode
        True,  # WEST: L3
    ],
    dtype=bool,
)

GAME_STATES = ("Auto", "Teleop", "Endgame", "End")
TASK_PHASES = ("seek", "intake", "carry", "align", "score")
SCORE_KEYS = (
    "coral_points",
    "trough_points",
    "net_points",
    "processor_points",
    "climb_points",
    "park_points",
    "leave_points",
    "coral_scored",
    "algae_scored",
    "total_points",
)
