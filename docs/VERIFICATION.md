# Verification and acceptance

Fast Python tests do not require Unity:

```bash
scripts/test_python.sh
```

Build and exercise one real worker:

```bash
scripts/build_unity.sh server
export MOSIM_EXECUTABLE="$PWD/_Build/RL/LinuxServer/MoSimRL.x86_64"
MOSIM_PYTEST_MARKS=integration scripts/test_python.sh
MOSIM_RUN_SLOW_INTEGRATION=1 MOSIM_PYTEST_MARKS=integration scripts/test_python.sh
```

Run Unity EditMode tests from the command line:

```bash
"$UNITY_EDITOR" -batchmode -projectPath "$PWD" \
  -runTests -testPlatform EditMode -testResults _Build/RL/editmode-results.xml \
  -logFile _Build/RL/editmode-tests.log
```

Benchmark process-level vectorization and retain the fastest successful worker count in `python/config/runtime.yaml`:

```bash
conda run -n mosim-rl mosim-benchmark
conda run -n mosim-rl mosim-soak --num-envs 8 --matches 2
```

The bounded pipeline acceptance run is:

```bash
conda run -n mosim-rl mosim-train --total-timesteps 100000
```

Each training run writes its resolved YAML configuration, Unity worker logs, Monitor data, TensorBoard events, final PPO checkpoint, and `VecNormalize` state beneath `runs/<timestamp>/`.

## Live visual debugging and W&B

Use the graphical development build to watch a random policy at a real-time 10 Hz decision rate:

```bash
conda run -n mosim-rl mosim-random --graphical --steps 2000
```

Drive the same 25-value gamepad Gym contract with a physical SDL-compatible controller:

```bash
conda run -n mosim-rl mosim-gamepad --curriculum-stage 4
```

Exercise every active teleop mapping with a repeatable graphical sequence:

```bash
conda run -n mosim-rl mosim-gamepad --scripted-demo --print-every 5
```

Verify production Team 118 ground acquisition (real overlap detection, force,
handoff, and possession) with a coral placed in the normal intake volume:

```bash
conda run -n mosim-rl mosim-gamepad --scripted-demo --pickup-test --print-every 5
```

For PPO, worker 0 can render locally while all TensorBoard/SB3 metrics and periodic model checkpoints sync to W&B:

```bash
conda run -n mosim-rl wandb login
conda run -n mosim-rl mosim-train --graphical --wandb \
  --wandb-project mosim-reefscape-rl --action-mode gamepad \
  --total-timesteps 100000
```

The Unity preview is a local window; W&B displays metrics, system utilization, run configuration, and checkpoints. Training preview worker 0 shows the exact command-stepped trajectory and pauses with the synchronized vector batch during PPO updates. Random, gamepad, and graphical evaluation sessions instead use continuous client-style real time. Use headless mode for maximum training throughput.

## Local acceptance result

The 2026-07-09 acceptance run passed against Unity 2023.2.22f1:

- Real-worker Gymnasium and SB3 environment checkers passed.
- The semantic and NitroGen-compatible gamepad environments both pass their real-worker checkers; the connected X360 controller also completed a graphical rollout.
- A coherent random gamepad actor completed all 1,560 decisions of a natural match, earned the 3-point AUTO leave score, and terminated at 156 simulated seconds. The scripted teleop sequence verified drive, strafe, yaw, L1-L4, intake, place, station toggle, and stow outputs.
- A complete episode terminated naturally after 156.0 simulated seconds.
- Eight workers each completed two matches without deadlocks, contamination, or NaNs: 24,960 transitions at 220.4 aggregate steps/s.
- The 1/2/4/8/12/16 sweep measured 83.3/142.8/172.8/239.8/369.0/396.8 aggregate steps/s. Sixteen workers was the fastest stable count on this 32-thread host.
- PPO completed 106,496 transitions (the first whole rollout beyond the requested 100,000) with 16 workers and CUDA, saving all required artifacts.

The 2026-07-10 client-fidelity regression run additionally verified:

- RL reset no longer rewrites or temporarily freezes articulated child rigidbodies; it uses MoSimulator's production `ResetMatch()` reconstruction path.
- Graphical reset and idle simulation remain at 1x and preserve MoSimulator's native approximately 222.2 Hz physics. The former forced 50 Hz solver step was 4.44x too large and destabilized robot mechanisms, the drivetrain, and suspended cages together.
- Team 118 acquired a released physical coral through the production ground-intake pipeline and advanced from intake state 1 to stored state 4 in both server and graphical players.
- RT release is checked for both the official preload and an intake-acquired coral; each must transition possession from stored state 4 to state 0 in one policy decision.
- The graphical idle arm peak-to-peak motion fell from about 5.87 degrees under the forced 50 Hz step to 0.045 degrees at the native timestep (about 130x lower). The headless regression measured 0.004 degrees and zero cage angular speed after settling.
- 24 fast tests, five real-player integration/checker cases, and the complete 156-second match test passed; the bounded graphical pickup run exited without Unity errors or orphaned processes.
