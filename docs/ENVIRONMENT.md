# Reefscape coral environment contract

`MoSim-Reefscape-Coral-v0` and `MoSim-Reefscape-Gamepad-v0` are state-based, single-agent Gymnasium environments controlling blue Team 118. Physics retains MoSimulator's native approximately 222.2 Hz timestep. The default five 20 ms control quanta give a 10 Hz policy rate; fractional native-step timing error is carried between decisions.

## Gamepad action (recommended)

`MoSim-Reefscape-Gamepad-v0` uses the NitroGen-compatible flat layout `Box(low, high, (25,), float32)`:

| Slice/index | Meaning |
|---:|---|
| `0:2` | Left-stick X/Y; strafe and forward drive |
| `2:4` | Right-stick X/Y; X controls yaw and Y is retained for NitroGen compatibility |
| `4:25` | NitroGen button order: BACK, D-pad down/left/right/up, B, guide, LB, L3, LT, Y, right-stick down/left/right, RB, R3, RT, right-stick up, A, start, X |

Axes are in `[-1, 1]`; buttons and triggers are in `[0, 1]` and buttons activate above `0.5`. The complete physical-controller mapping is:

| Control | Command |
|---|---|
| Left stick | Translate using the selected robot- or field-oriented drive mode |
| Right-stick X | Rotate |
| A / B / X / Y (coral) | L1 / L2 / L3 / L4 |
| A (algae) | Stack pickup when empty; processor scoring when holding algae |
| B / X (algae) | Low / high reef pickup |
| Y (algae) | Barge (net) scoring when holding algae |
| LT | Hold intake/roller; B or X remains selected while collecting reef algae |
| RT | Score/place at the selected position |
| D-pad down | Stow |
| D-pad up | Toggle coral/algae mode |
| D-pad left | Toggle normal/L1 intake mode |
| D-pad right | Unbound; reserved for fixed 25D schema compatibility |
| LB / RB | Hold auto-align left/right |
| Left-stick click | Unbound; climber disabled, channel reserved for 25D compatibility |
| Right-stick click | Flip the robot camera |
| Start | Reset the controller-driving episode |
| Back/Share | Exit the controller-driving example |

Climb bindings are removed from both Unity players and the keyboard. D-pad down
only stows; left-stick click does nothing, including for direct RL commands.
Unbound and unused NitroGen outputs remain masked inactive in
`info["gamepad_active_mask"]`. The physical-controller reader leaves the
D-pad-Right and left-stick-click channels at zero.
The native Unity input asset also leaves D-pad Right unbound for both players.

The adapter preserves selected levels between button presses and converts physical or policy gamepad output into the internal six-value robot command. It also sends the raw gamepad action to Unity for edge-triggered controls. `info` reports both `gamepad_action` and `semantic_action`. Observation indices `56:62` contain the executed six-value semantic command, not all 25 policy outputs.

### Teleop demonstration dataset

`python/main/data_collection_teleop.py` writes the operator's complete 25-value
controller vector to the LeRobot `action` feature. This preserves every stick,
trigger, face button, D-pad mode, auto-align bumper, stick click, Start, and
Back channel. The translated six-value command is stored separately as
`action.semantic`; its fourth name is `target_setpoint`, because the value can
represent coral levels or algae pickup/scoring positions. The collector
defaults to `camera_mode=field` and `drive_mode=field` on every reset. Start and
Back are recording controls, so their pressed frames end/reset recording before
an environment transition is added; the fixed action channels remain present.
Manual control uses an independent 20 ms stream (50 Hz), while synchronized
dataset samples default to 8 FPS for the three 640x360 cameras. Unity returns
the exact raw and semantic actions active when it snapshots state and submits
all camera renders. `metadata.sample` stores `[sim_time, control_sequence]`, and
the client rejects any camera whose simulation timestamp differs from the state
timestamp. The default 156-second safety limit matches MoSim's verified full
episode: the 150-second FRC clock, three-second auto-to-teleop pause, and
three-second final-scoring grace.

Recording additionally verifies the sample ID, Unity frame number, and applied
control sequence shared by all three images and the control snapshot.
`metadata.capture` retains these three IDs as exact int64 values; duplicate or
out-of-order captures are rejected. State and image rendering are captured
together in late `LateUpdate`, after gameplay consumes input. Action labels mean
the command active at capture time, not the command that caused all motion since
the preceding recorded frame. The 8 FPS videos sample the independent 50 Hz
control stream; short presses between video frames are not a full control log.
Video timestamps use the requested FPS. If capture falls behind, the collector
warns about faster-than-realtime playback; `metadata.sample` retains the actual
simulation capture times. Matching rows/images are not duplicated to fill gaps.

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
