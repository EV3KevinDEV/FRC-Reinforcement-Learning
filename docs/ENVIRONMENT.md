# Reefscape coral environment contract

`MoSim-Reefscape-Coral-v0` and `MoSim-Reefscape-Gamepad-v0` are state-based, single-agent Gymnasium environments controlling blue Team 118. Physics retains MoSimulator's native approximately 222.2 Hz timestep. The default five 20 ms control quanta give a 10 Hz policy rate; fractional native-step timing error is carried between decisions.

## Gamepad action (recommended)

`MoSim-Reefscape-Gamepad-v0` uses the NitroGen-compatible flat layout `Box(low, high, (25,), float32)`:

| Slice/index | Meaning |
|---:|---|
| `0:2` | Left-stick X/Y; strafe and forward drive |
| `2:4` | Right-stick X/Y; X controls yaw and Y is retained for NitroGen compatibility |
| `4:25` | NitroGen button order: BACK, D-pad down/left/right/up, B, guide, LB, L3, LT, Y, right-stick down/left/right, RB, R3, RT, right-stick up, A, start, X |

Axes are in `[-1, 1]`; buttons and triggers are in `[0, 1]` and buttons activate above `0.5`. The active Team 118 mappings are A/B/X/Y = L1/L2/L3/L4, LT = intake, RT = place, D-pad down = stow, and D-pad right = toggle ground/station source. Other buttons remain in the fixed layout but are masked inactive in `info["gamepad_active_mask"]`.

The adapter preserves selected levels between button presses and converts physical or policy gamepad output into the internal six-value robot command. `info` reports both `gamepad_action` and `semantic_action`. Observation indices `56:62` contain the executed six-value semantic command, not all 25 policy outputs.

## Legacy semantic action

`MoSim-Reefscape-Coral-v0` retains the original `Box(-1, 1, (6,), float32)` contract for existing checkpoints:

| Index | Meaning |
|---:|---|
| 0 | Robot-relative forward command |
| 1 | Robot-relative left strafe command |
| 2 | Counter-clockwise yaw command |
| 3 | Six-bin target: stow, intake, L1, L2, L3, or L4 |
| 4 | Intake above `+0.33`, idle between thresholds, place pulse on a crossing below `-0.33` |
| 5 | Ground source at or below zero, station source above zero; station is ignored in AUTO |

## Observation

The observation is `Box(-1, 1, (62,), float32)` with a fixed ordering:

| Slice | Count | Fields |
|---|---:|---|
| `0:12` | 12 | Field x/z, yaw sin/cos, local x/z velocity, yaw rate, up xyz, grounded, enabled |
| `12:20` | 8 | Setpoint, arm, elevator, intake, algae arms, coral possession/state, station mode |
| `20:25` | 5 | One-hot task phase: seek, intake, carry, align, score |
| `25:31` | 6 | Relative coral x/z, distance, velocity x/z, valid flag |
| `31:36` | 5 | Relative target x/z, distance, heading-error sin/cos |
| `36:40` | 4 | One-hot target level L1-L4 |
| `40:45` | 5 | Match time and one-hot AUTO/TELEOP/endgame/end state |
| `45:55` | 10 | Branch coral, trough, net, processor, climb, park, leave, coral count, algae count, total |
| `55:56` | 1 | Last raw score delta |
| `56:62` | 6 | Previous action |

All scalar quantities are normalized and clipped to `[-1, 1]`. Non-finite telemetry is rejected.

## Virtual-camera API

Virtual cameras are an auxiliary API and do not change the 62-value state observation. After reset, `env.list_virtual_cameras()` returns typed configuration/calibration records and `env.get_virtual_camera_frame(name)` returns a typed JPEG frame. Capture requires the graphical development player; it does not advance simulation time and cannot overlap an in-flight environment step.

See [Robot virtual cameras](VIRTUAL_CAMERAS.md) for prefab configuration, Python examples, constraints, and cited Unity rendering behavior. RGB image observations and visual encoder policies are not part of the current environment contract.

## Episode and score

A natural episode covers 15 seconds of AUTO, the 3-second disabled transition, 135 seconds of TELEOP/endgame, and a final 3-second scoring grace. Match completion returns `terminated=True`; transport and worker failures return `truncated=True`.

Official Table 6-2 values represented by the score snapshot are:

| Category | AUTO | TELEOP |
|---|---:|---:|
| Leave | 3 | — |
| L1 trough | 3 | 2 |
| L2 | 4 | 3 |
| L3 | 6 | 4 |
| L4 | 7 | 5 |
| Processor algae | — | 6 |
| Net algae | — | 4 |
| Park / shallow / deep cage | — | 2 / 6 / 12 |

`info` includes worker/protocol IDs, curriculum stage and name, target level, raw mechanism possession/state, cage linear/angular-speed diagnostics, raw score, score delta, reward terms, cycle success, game/simulation time, and termination reason.

## Reward

- Five times the change in branch coral, trough, and leave points.
- `+5` on acquisition and `+10` on successful scoring subgoal.
- Potential progress of `+2` per metre and, while carrying, `+0.5` per radian of heading improvement.
- `-5` for a drop and `-25` for tipping or leaving the field.
- `-0.002 ||drive||²` effort and `-0.01 ||a_t-a_(t-1)||²` action-rate penalties.
