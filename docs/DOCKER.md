# Docker runtime

The Docker image runs the Python Gymnasium environment and a prebuilt Linux
Dedicated Server player in the same container. Unity Hub, the Unity Editor, and
Unity license credentials are deliberately excluded from the image.

## Supported host matrix

| Host | Runtime support | Notes |
|---|---|---|
| Linux x86-64 | Native | Fully exercised by the local build and smoke workflow |
| Windows x86-64 | Docker Desktop Linux containers | Uses the same `linux/amd64` image |
| macOS Intel | Docker Desktop | Uses the same `linux/amd64` image |
| Apple Silicon macOS | Emulated/experimental | Docker Desktop must emulate `linux/amd64`; slower and not verified as native |
| Windows ARM | Emulated/experimental | Requires Docker's `linux/amd64` emulation |
| Native Linux ARM64 | Not supported | The Unity player is compiled for x86-64 |

The image is pinned to `linux/amd64` because Unity's generated
`MoSimRL.x86_64` player is architecture-specific. Docker provides a consistent
Linux userspace across supported Docker Engine/Desktop hosts, but this does not
make the Unity executable architecture-independent.

## Prerequisites

1. Install Docker Engine with BuildKit/Compose on Linux, or Docker Desktop on
   Windows/macOS.
2. Build the Unity Linux Dedicated Server player at
   `_Build/RL/LinuxServer/MoSimRL.x86_64`.

On Linux, build the player with:

```bash
scripts/build_unity.sh server
```

On Windows or macOS, install **Linux Dedicated Server Build Support** for Unity
`2023.2.22f1`, then invoke the project method `MoSimRlBuild.BuildLinuxServer`
from the editor or Unity batch mode. The generated directory must be copied to
`_Build/RL/LinuxServer` before building the container.

The Unity build is not committed to Git and is not downloaded implicitly. This
keeps generated simulator assets and large platform binaries out of source
control and makes the person building the image responsible for the applicable
license and redistribution terms.

## Cross-platform helper commands

Linux or macOS:

```bash
scripts/docker.sh test
scripts/docker.sh build
scripts/docker.sh smoke
```

Native Windows Command Prompt or PowerShell:

```bat
scripts\docker.bat test
scripts\docker.bat build
scripts\docker.bat smoke
```

- `test` builds the Python dependency/test target and runs all non-integration
  tests in the container. It does not require a Unity build.
- `build` packages the local Linux Unity server into `mosim-rl:local`.
- `smoke` runs both the containerized Python tests and a real 20-step Unity
  Gymnasium environment check.

Set `MOSIM_DOCKER_IMAGE` to change the image tag or
`MOSIM_UNITY_SERVER_DIR` to package a player from another location.

## Docker Compose

Create the local runs directory, build the image, and run the smoke command:

```bash
mkdir -p runs
docker compose build mosim
docker compose run --rm mosim
```

For a portable CPU training run with TensorBoard exposed on the host:

```bash
docker compose run --rm --service-ports mosim \
  mosim-train --device cpu --num-envs 4 --total-timesteps 100000 \
  --tensorboard-host 0.0.0.0
```

Open <http://127.0.0.1:6006>. Run artifacts are written to the host's `runs/`
directory. Stop training with Ctrl-C; Compose's init process forwards the signal
and the Python launcher shuts down its Unity workers.

The portable image intentionally defaults to CPU. The current policy is an MLP,
for which Stable-Baselines3 generally obtains poor GPU utilization. GPU container
support additionally depends on host-specific NVIDIA drivers and container
runtime configuration and is outside this portable contract. The Docker build
installs PyTorch from its CPU wheel index so CUDA runtime libraries are not
silently added to the portable image.

## Direct Docker build

The generated Unity player is supplied as a named BuildKit context:

```bash
docker build --platform linux/amd64 --target runtime \
  --build-context unity_server=./_Build/RL/LinuxServer \
  --tag mosim-rl:local .
```

The named context is intentional: `.dockerignore` excludes the Unity source tree
and generated builds from the ordinary Docker context, keeping transfers small
and preventing an accidental broad copy of the repository into the image.

## What is and is not verified

The automated smoke check verifies that:

- Python dependencies import inside the container;
- all non-integration Python tests pass inside the container;
- the packaged Unity player starts headlessly;
- Python connects to the RL bridge;
- Gymnasium reset/step returns finite, in-space observations and rewards;
- simulation time advances by the configured decision interval without a worker
  truncation or unexpected task termination;
- the Unity worker is stopped when the command exits.

It does not verify graphical rendering or virtual-camera capture. The runtime
image is headless, and camera capture requires the graphical development player.
For that reason, the optional OpenCV camera-preview dependency is also excluded
from the portable runtime image.
It also cannot prove behavior on every Docker Desktop version or CPU architecture;
the host table above separates native support from emulation explicitly.

The deployment smoke test intentionally does not call Gymnasium's exact seeded
step-determinism check. MoSimulator's PhysX articulations and contacts settle
within explicit tolerances but are not bitwise deterministic across scene
reconstruction; the real-Unity integration tests compare seeded trajectories
with those documented tolerances instead.
