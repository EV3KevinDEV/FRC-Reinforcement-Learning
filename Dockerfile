# syntax=docker/dockerfile:1.7

ARG PYTHON_IMAGE=python:3.11-slim-bookworm
ARG TORCH_INDEX_URL=https://download.pytorch.org/whl/cpu

FROM ${PYTHON_IMAGE} AS python-base

ARG TORCH_INDEX_URL

LABEL org.opencontainers.image.title="MoSimulator Reefscape RL" \
      org.opencontainers.image.description="Headless Gymnasium/PPO runtime for the MoSimulator Reefscape RL bridge" \
      org.opencontainers.image.source="https://github.com/EV3KevinDEV/FRC-Reinforcement-Learning" \
      org.opencontainers.image.licenses="GPL-3.0-only"

ENV DEBIAN_FRONTEND=noninteractive \
    PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    SDL_AUDIODRIVER=dummy

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        libasound2 \
        libgl1 \
        libglib2.0-0 \
        libglu1-mesa \
        libnss3 \
        libpulse0 \
        libx11-6 \
        libx11-xcb1 \
        libxcursor1 \
        libxext6 \
        libxi6 \
        libxinerama1 \
        libxrandr2 \
        libxrender1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace
COPY python/ ./python/
RUN --mount=type=cache,target=/root/.cache/pip \
    python -m pip install --upgrade pip \
    && python -m pip install --index-url "$TORCH_INDEX_URL" 'torch==2.12.0' \
    && python -m pip install --editable './python'

RUN groupadd --gid 1000 mosim \
    && useradd --uid 1000 --gid mosim --create-home --shell /bin/bash mosim \
    && mkdir -p /workspace/runs /workspace/logs \
    && chown -R mosim:mosim /workspace

FROM python-base AS test

RUN --mount=type=cache,target=/root/.cache/pip \
    python -m pip install --editable './python[test]'
USER mosim
CMD ["pytest", "python/tests", "-m", "not integration"]

FROM python-base AS runtime

# The named BuildKit context deliberately keeps the generated 184+ MiB Unity
# player out of Git and the normal source context. Build commands must provide:
#   --build-context unity_server=./_Build/RL/LinuxServer
COPY --from=unity_server --chown=mosim:mosim / /opt/mosim/unity/
RUN test "$(dpkg --print-architecture)" = "amd64" \
    && test -x /opt/mosim/unity/MoSimRL.x86_64

ENV MOSIM_EXECUTABLE=/opt/mosim/unity/MoSimRL.x86_64
USER mosim
STOPSIGNAL SIGTERM
CMD ["mosim-smoke", "--steps", "20"]
