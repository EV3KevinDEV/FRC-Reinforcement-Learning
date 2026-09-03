# MoSimulator Reefscape RL

> **Independent research fork.** This repository is not the official MoSimulator repository and is not endorsed by Cascade Studios LLP, FIRST, or any referenced FRC team. Product, event, robot, and team names are used only to identify the systems studied; associated names, marks, and assets remain the property of their respective owners.

This repository adds a versioned Gymnasium/Stable-Baselines3 bridge to MoSimulator `v26.2.0` for state-based, vectorized PPO training with the blue Team 118 Robonauts robot. Eight independent Linux Dedicated Server players are the default vectorization model.

The upstream basis is the pinned [`MoSimulator-Public` `v26.2.0` snapshot](https://github.com/MoSimulator/MoSimulator-Public/tree/v26.2.0), commit [`9dd3d7d`](https://github.com/MoSimulator/MoSimulator-Public/commit/9dd3d7d1d04529d82c98d049f2fc273ebb1e7213). That tagged snapshot identifies itself as GPL-3.0 software and contains the GPL-3.0 license used by this fork.[^upstream-snapshot] This repository is a modified work, with RL, training, testing, documentation, and tooling changes made in 2026; see [NOTICE.md](NOTICE.md) for the detailed provenance and modification notice.

As of August 11, 2026, the [current upstream `main` branch license](https://github.com/MoSimulator/MoSimulator-Public/blob/main/LICENSE) uses different, restrictive terms. Do not merge or copy post-`v26.2.0` upstream material into this fork without a separate license review. The presence of research code or a citation does not itself clear third-party assets, trademarks, generated datasets, model weights, or publication screenshots.

The Unity project remains pinned to `2023.2.22f1`. `origin` is this research fork and `mosim-upstream` identifies the upstream repository; the latter should be treated as reference-only unless the license of a specific source snapshot has been reviewed.

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

### Native Windows setup

From Command Prompt or PowerShell on Windows 10/11, run:

```bat
scripts\setup_windows.bat all
```

The script checks for 25 GiB of free space; installs Miniconda and Unity Hub with
`winget` when needed; creates the `mosim-rl` Python environment, including test
and virtual-camera dependencies; installs Unity `2023.2.22f1` with Windows
Dedicated Server Build Support (Unity Hub module
[`windows-server`](https://docs.unity.com/en-us/hub/hub-cli-reference#available-modules)); builds native Windows server and development
players; runs the Python tests; and performs a short real-Unity smoke test. It
also saves `MOSIM_EXECUTABLE` for the current Windows user.

Unity Hub sign-in and license activation cannot be completed safely by the
script. If Hub needs either one, the script opens Hub and tells you to rerun the
same command afterward. Useful partial modes are:

```bat
scripts\setup_windows.bat setup
scripts\setup_windows.bat build
scripts\setup_windows.bat test
scripts\setup_windows.bat all -SkipSmokeTest
```

`setup` installs dependencies without building, `build` performs setup plus both
Unity builds and validation, and `test` reruns the Python tests. The Windows
commands use native `.exe` players; do not run this script inside WSL or Git Bash.

### Docker setup

A headless `linux/amd64` image packages the Python environment and a locally
built Linux Dedicated Server player. After building that player, run:

```bash
scripts/docker.sh smoke       # Linux/macOS
scripts\docker.bat smoke      # Windows with Docker Desktop
```

The smoke workflow runs the Python suite inside Docker and then exercises the
real Unity Gym environment for 20 steps. The same image runs through Docker
Desktop on x86-64 Windows and macOS; ARM hosts require slower `linux/amd64`
emulation. See [the Docker runtime guide](docs/DOCKER.md) for prerequisites,
Compose training commands, the support matrix, and verification boundaries.

Training starts a TensorBoard server automatically at
`http://127.0.0.1:6006` and writes its output to the run directory. Use
`--tensorboard-port PORT` or `--tensorboard-host HOST` to change the listener,
or `--no-tensorboard` when only event-file logging is wanted. The server stops
when training exits.

The graphical development player is built at `_Build/RL/LinuxDevelopment/MoSimRL.x86_64` for visual debugging.

Robot-mounted virtual cameras can be added from **MoSimulator > RL > Virtual Camera Tool** in the Unity editor. A graphical environment can then call `list_virtual_cameras()` and `get_virtual_camera_frame("front")`; the returned object contains decoded JPEG bytes and a `.save(path)` helper. Run `mosim-camera-preview --camera <camera-id>` for a live OpenCV view. See [the virtual-camera guide](docs/VIRTUAL_CAMERAS.md) for the complete setup, API, limits, and source citations.

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
| `python python/main/data_collection_teleop.py --dataset-root output/mosim-teleop-run1` | Record LeRobot v3 teleop episodes with field camera/drive defaults, three robot cameras, the full 25D gamepad action, and the translated 6D command |
| `python python/main/data_collection_teleop.py --windowed-fullscreen --dataset-root output/mosim-teleop-run1` | Record teleop with the Unity player filling the desktop in a borderless window |
| `mosim-train --action-mode gamepad --total-timesteps 100000` | Train vectorized PPO with the recommended 25D controller output and a local TensorBoard server |
| `mosim-train --action-mode semantic --total-timesteps 100000` | Train with the legacy six-value action contract |
| `mosim-train --graphical --wandb --action-mode gamepad` | Train while rendering worker 0 and syncing metrics/checkpoints to W&B |
| `mosim-evaluate runs/<run>/ppo_final.zip --vecnormalize runs/<run>/vecnormalize.pkl --action-mode gamepad --graphical` | Evaluate a gamepad checkpoint visually |
| `mosim-benchmark --workers 1 2 4 8 12 16 --decisions 100` | Benchmark worker-count throughput and update `runtime.yaml` |
| `mosim-soak --num-envs 8 --matches 2` | Run the multi-worker deadlock/NaN/full-match soak test |

The teleop collector uses the same Team 118 layout as `controller_driver_control`:
A/B/X/Y select coral L1-L4; algae B/X select low/high reef pickup, A selects
stack pickup or processor, and Y selects the barge; LT runs intake, RT scores,
LB/RB auto-align, and the D-pad controls stow and robot/intake/source modes.
Start saves the current episode early and resets, while Back saves and exits.
Each new dataset root must not already exist.
Both the collector and `python/examples/05_controller_driver_control.py` accept
`--windowed-fullscreen`; without it, the Unity player remains a 1280x720 window.
Manual control is sent independently at the normal 50 Hz FRC cadence. The
collector defaults to 8 FPS for synchronized 640x360 state/action/three-camera
samples; every row stores the exact action Unity had applied when it captured
the state and images. Collector episodes allow 156 seconds: the complete
150-second match clock, MoSim's three-second disabled transition, and its
three-second final-scoring grace.

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

# TensorBoard starts with training by default; this still starts it manually
tensorboard --logdir runs
wandb login
```

Useful flags shared by the rollout/training tools include `--seed`, `--curriculum-stage 0..4`, `--executable PATH`, and `--action-mode gamepad|semantic`. Run any command with `--help` for its full argument list.

Runnable examples of `gym.make`, `reset`/`step`, physical-controller driving, virtual-camera capture, vectorized workers, and PPO checkpoint rollout are in [`python/examples`](python/examples/README.md).

See [the environment contract](docs/ENVIRONMENT.md), [wire protocol](docs/PROTOCOL.md), [virtual-camera guide](docs/VIRTUAL_CAMERAS.md), [verification guide](docs/VERIFICATION.md), [research-publication checklist](docs/RESEARCH_PUBLICATION.md), and [roadmap](ROADMAP.md).

## Architecture

Python sends all vector actions before waiting for any worker. Each isolated Unity process applies one action to Team 118 for a 100 ms decision window while preserving MoSimulator's native approximately 4.5 ms PhysX step, then returns immutable robot/task/match/score telemetry. The Python wrapper encodes a 62-value observation, computes reward, and adapts Gymnasium's five-value transition to SB3's vector API with `terminal_observation` on automatic reset.

This is process-level CPU vectorization using headless Dedicated Server players. MoSimulator's global singletons and scene-wide discovery currently prevent Isaac Lab-style GPU scene replication.

## Citation

Research publications should cite both the upstream simulator snapshot and this RL fork. A machine-readable citation for the fork is provided in [`CITATION.cff`](CITATION.cff); GitHub uses this file to generate APA and BibTeX citations.[^github-citation]

Suggested software references:

1. Cascade Studios. (2026). *MoSimulator Public Repository* (Version v26.2.0) [Computer software]. GitHub. https://github.com/MoSimulator/MoSimulator-Public/tree/v26.2.0
2. EV3KevinDEV. (2026). *MoSimulator Reefscape RL* (Version 0.1.0) [Computer software]. GitHub. https://github.com/EV3KevinDEV/FRC-Reinforcement-Learning-

```bibtex
@software{cascade_studios_mosimulator_2026,
  author  = {{Cascade Studios}},
  title   = {MoSimulator Public Repository},
  version = {v26.2.0},
  year    = {2026},
  url     = {https://github.com/MoSimulator/MoSimulator-Public/tree/v26.2.0},
  note    = {Upstream source snapshot, commit 9dd3d7d1d04529d82c98d049f2fc273ebb1e7213}
}

@software{ev3kevindev_mosimulator_reefscape_rl_2026,
  author  = {{EV3KevinDEV}},
  title   = {MoSimulator Reefscape RL},
  version = {0.1.0},
  year    = {2026},
  url     = {https://github.com/EV3KevinDEV/FRC-Reinforcement-Learning-}
}
```

For a submitted paper, cite an archived release or exact commit rather than a moving branch. If a DOI-backed archive is created, replace the fork URL above with that DOI. The repository does not contain a verified personal name or ORCID for the maintainer, so the current citation uses the GitHub handle `EV3KevinDEV`; replace it with verified contributor metadata before release when appropriate.

## License, modifications, and AI assistance

The code in this fork is distributed under the repository's [GNU GPL version 3 license](LICENSE). GPLv3 requires a modified work to carry prominent modification and license notices, and distribution of executable artifacts can require the corresponding source.[^gpl-modified][^gpl-source] The repository source, build scripts, `LICENSE`, and [NOTICE.md](NOTICE.md) should therefore accompany any published software artifact.

Substantial portions of the RL extension, tests, tooling, and documentation were developed with generative-AI assistance, including OpenAI Codex—informally, this project has been partly “vibe coded.” AI-generated suggestions were selected and integrated under maintainer direction, but that fact is not evidence of correctness or independent review. Any research author using this software must perform and report their own validation and follow the target venue's current AI-disclosure policy. See the [research-publication checklist](docs/RESEARCH_PUBLICATION.md) for a disclosure template and the remaining rights-clearance steps.

This documentation improves attribution and license transparency; it is not legal advice and does not guarantee that a particular paper, dataset, model, screenshot, or binary release is cleared for publication. Obtain review from your institution or qualified counsel when publication includes third-party visual assets, branding, or redistributed executables.

## Upstream resources

- Upstream snapshot used here: [`MoSimulator-Public` `v26.2.0`](https://github.com/MoSimulator/MoSimulator-Public/tree/v26.2.0)
- Official MoSimulator website: [mosimulator.com](https://mosimulator.com/)
- Official modding documentation: [docs.mosimulator.com](https://docs.mosimulator.com/)

[^upstream-snapshot]: Cascade Studios, “MoSimulator Public Repository,” version `v26.2.0`, GitHub, 2026. The tagged repository README identifies GPL-3.0, and the tag contains the corresponding [`LICENSE`](https://github.com/MoSimulator/MoSimulator-Public/blob/v26.2.0/LICENSE).
[^github-citation]: GitHub, “[About CITATION files](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-citation-files),” accessed August 11, 2026.
[^gpl-modified]: Free Software Foundation, “[GNU General Public License, version 3, §5: Conveying Modified Source Versions](https://www.gnu.org/licenses/gpl-3.0.html#section5),” 2007.
[^gpl-source]: Free Software Foundation, “[Frequently Asked Questions about the GNU Licenses: Distribution of programs released under the GNU licenses](https://www.gnu.org/licenses/gpl-faq.html#GPLRequireSourcePostedPublic),” accessed August 11, 2026.
