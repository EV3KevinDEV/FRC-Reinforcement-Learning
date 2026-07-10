# MoSimulator Reefscape RL

This branch adds a versioned Gymnasium/Stable-Baselines3 bridge to MoSimulator `v26.2.0` for state-based, vectorized PPO training with the blue Team 118 Robonauts robot. Eight independent Linux Dedicated Server players are the default vectorization model.

The Unity project remains pinned to `2023.2.22f1`. `origin` remains the project fork and `mosim-upstream` tracks the public simulator repository.

## Setup

At least 25 GiB free space is required before Unity import/build and environment creation.

```bash
scripts/preflight.sh
scripts/create_conda_env.sh
scripts/install_unity.sh
```

Unity Hub sign-in and license activation are the only manual checkpoint. After activation:

```bash
scripts/build_unity.sh all
export MOSIM_EXECUTABLE="$PWD/_Build/RL/LinuxServer/MoSimRL.x86_64"
conda run -n mosim-rl mosim-random --steps 2000 --check-env
conda run -n mosim-rl mosim-benchmark
conda run -n mosim-rl mosim-train --total-timesteps 100000
```

The graphical development player is built at `_Build/RL/LinuxDevelopment/MoSimRL.x86_64` for visual debugging.

To watch a random policy in real time:

```bash
conda run -n mosim-rl mosim-random --graphical --steps 2000
```

New policies use a NitroGen-compatible gamepad action by default: left stick drive, right-stick X yaw, A/B/X/Y select L1-L4, LT intakes, RT places, D-pad down stows, and D-pad right toggles the coral source. Existing six-action checkpoints remain available with `--action-mode semantic`.

To drive the exact Gymnasium environment with a connected Xbox/PlayStation-compatible controller:

```bash
conda run -n mosim-rl mosim-gamepad
```

This opens the graphical player with an empty robot and runs until Ctrl-C. Use `--with-preload` for the official starting coral, `--controller 1` for a second gamepad, `--deadzone 0.15` to tune stick drift, or `--steps 1000` for a bounded test.

To verify Team 118's real ground-intake physics, run the scripted acquisition fixture. It first prints and displays an empty robot for 30 decisions, then holds the standard LT binding on a physical coral in the production intake volume and prints `has_coral`/`coral_state` while the graphical client retains MoSimulator's native approximately 222.2 Hz physics:

```bash
conda run -n mosim-rl mosim-gamepad --scripted-demo --pickup-test --print-every 5
```

Graphical random, gamepad, and evaluation sessions use continuous real-time physics between bridge requests, matching normal client behavior for articulated mechanisms, loose field pieces, and cages. Training—including its graphical worker—uses the same native physics timestep and holds each action for five 20 ms control quanta, pausing only between requests so no vector worker can run ahead of PPO; the preview is therefore the exact training trajectory rather than a separate animation.

To watch worker 0 while training and stream TensorBoard/PPO metrics to Weights & Biases:

```bash
conda run -n mosim-rl wandb login
conda run -n mosim-rl mosim-train --graphical --wandb \
  --wandb-project mosim-reefscape-rl --action-mode gamepad \
  --total-timesteps 100000
```

Graphical training uses the development player for every worker, but only worker 0 opens a window; the other workers still launch headlessly. W&B receives metrics and model checkpoints, while the live rendered preview remains in the local Unity window.

## CLI launch reference

Activate the environment once per terminal before using the shorter commands below:

```bash
conda activate mosim-rl
```

| Command | Purpose |
|---|---|
| `mosim-random --graphical --action-mode gamepad --episodes 1 --steps 2000` | Run one realistic random-controller episode with a local Unity window |
| `mosim-random --action-mode gamepad --steps 2000 --print-every 100` | Run the same random actor quickly in the headless server |
| `mosim-gamepad` | Teleoperate the graphical Gym environment with controller 0 until Ctrl-C |
| `mosim-gamepad --with-preload` | Teleoperate with the official preloaded coral instead of the default empty robot |
| `mosim-gamepad --controller 1 --deadzone 0.15 --steps 1000 --print-every 1` | Test another controller and print every gamepad/robot command |
| `mosim-gamepad --scripted-demo --print-every 5` | Visually exercise every active controller mapping without touching a gamepad |
| `mosim-gamepad --scripted-demo --pickup-test --print-every 5` | Visually prove coral acquisition through Team 118's normal ground intake and then exercise every mapping |
| `mosim-train --action-mode gamepad --total-timesteps 100000` | Train vectorized PPO with the recommended 25D controller output |
| `mosim-train --action-mode semantic --total-timesteps 100000` | Train with the legacy six-value action contract |
| `mosim-train --graphical --wandb --action-mode gamepad` | Train while rendering worker 0 and syncing metrics/checkpoints to W&B |
| `mosim-evaluate runs/<run>/ppo_final.zip --vecnormalize runs/<run>/vecnormalize.pkl --action-mode gamepad --graphical` | Evaluate a gamepad checkpoint visually |
| `mosim-benchmark --workers 1 2 4 8 12 16 --decisions 100` | Benchmark worker-count throughput and update `runtime.yaml` |
| `mosim-soak --num-envs 8 --matches 2` | Run the multi-worker deadlock/NaN/full-match soak test |

Build and verification commands:

```bash
# Dedicated server, graphical development player, or both
scripts/build_unity.sh server
scripts/build_unity.sh development
scripts/build_unity.sh all

# Fast Python tests and real-Unity integration tests
scripts/test_python.sh
export MOSIM_EXECUTABLE="$PWD/_Build/RL/LinuxServer/MoSimRL.x86_64"
MOSIM_PYTEST_MARKS=integration scripts/test_python.sh
MOSIM_RUN_SLOW_INTEGRATION=1 MOSIM_PYTEST_MARKS=integration scripts/test_python.sh

# Local dashboards
tensorboard --logdir runs
wandb login
```

Useful flags shared by the rollout/training tools include `--seed`, `--curriculum-stage 0..4`, `--executable PATH`, and `--action-mode gamepad|semantic`. Run any command with `--help` for its full argument list.

See [the environment contract](docs/ENVIRONMENT.md), [wire protocol](docs/PROTOCOL.md), [verification guide](docs/VERIFICATION.md), and [roadmap](ROADMAP.md).

## Architecture

Python sends all vector actions before waiting for any worker. Each isolated Unity process applies one action to Team 118 for a 100 ms decision window while preserving MoSimulator's native approximately 4.5 ms PhysX step, then returns immutable robot/task/match/score telemetry. The Python wrapper encodes a 62-value observation, computes reward, and adapts Gymnasium's five-value transition to SB3's vector API with `terminal_observation` on automatic reset.

This is process-level CPU vectorization using headless Dedicated Server players. MoSimulator's global singletons and scene-wide discovery currently prevent Isaac Lab-style GPU scene replication.

## Upstream project

Welcome to the official repository for the public release of the MoSimulator source code. This source code allows you to build robot mods. For more information, visit the [MoSim website](https://mosimulator.com/).

## Modding Documentation
All of the modding documentation is located in a Google Doc here: [Modding Documentation](https://docs.mosimulator.com/).

<br>
Note: MoSimulator's source code is protected under a GNU GPL 3 License. Please follow its guidelines as you work on the game.
