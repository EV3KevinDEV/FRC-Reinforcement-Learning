# MoSimulator RL Python Package

This package implements the Gymnasium and Stable-Baselines3 side of the
MoSimulator Reefscape RL bridge. See the repository root `README.md` for setup,
Unity build, training, and evaluation instructions.

## Teleop dataset collection

`main/data_collection_teleop.py` records LeRobot v3 demonstrations from a
physical controller and the three Team 118 virtual cameras. It defaults to the
platform's graphical development player, a fixed-style field camera, and
field-oriented driving:

```powershell
python python/main/data_collection_teleop.py `
  --windowed-fullscreen `
  --dataset-root output/mosim-teleop-run1
```

`--windowed-fullscreen` fills the desktop with a borderless Unity window. Omit
it to retain the standard 1280x720 window.

The controller stream runs independently at 50 Hz, so camera encoding cannot
delay driving. The dataset defaults to 8 FPS, a sustainable rate for atomic
state/action samples plus all three 640x360 cameras. By default an episode allows
156 seconds: the complete 150-second FRC match clock plus MoSim's three-second
disabled auto-to-teleop transition and three-second final-scoring grace. The
environment still ends the episode when it reports completion.

The LeRobot `action` feature is the complete 25-value NitroGen-compatible
controller vector, including D-pad modes, auto-align bumpers, stick clicks,
triggers, Start, and Back. `action.semantic` retains the translated six-value
robot command for inspection. The presentation camera does not replace the
three robot-mounted images saved under `observation.images.*`. Start and Back
remain named channels for fixed-schema compatibility, but their pressed frames
are intercepted as recording controls before a robot transition is saved. Each
row uses the exact action returned by Unity for that state/image sample, while
`metadata.sample` stores simulation time and the realtime-control sequence for
alignment audits.
