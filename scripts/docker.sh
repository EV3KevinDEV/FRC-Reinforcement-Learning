#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mode="${1:-smoke}"
image="${MOSIM_DOCKER_IMAGE:-mosim-rl:local}"
unity_server="${MOSIM_UNITY_SERVER_DIR:-$repo_root/_Build/RL/LinuxServer}"

require_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    echo "Docker is required. Install Docker Engine or Docker Desktop and rerun this command." >&2
    exit 1
  fi
  docker version >/dev/null
}

build_test_image() {
  docker build \
    --platform linux/amd64 \
    --target test \
    --tag "${image}-test" \
    "$repo_root"
}

require_unity_server() {
  if [[ ! -x "$unity_server/MoSimRL.x86_64" ]]; then
    echo "Linux Unity server not found at $unity_server/MoSimRL.x86_64" >&2
    echo "On Linux, build it with: scripts/build_unity.sh server" >&2
    echo "On Windows/macOS, install Unity Linux Dedicated Server Build Support and run MoSimRlBuild.BuildLinuxServer." >&2
    exit 1
  fi
}

build_runtime_image() {
  require_unity_server
  docker build \
    --platform linux/amd64 \
    --target runtime \
    --build-context "unity_server=$unity_server" \
    --tag "$image" \
    "$repo_root"
}

run_tests() {
  build_test_image
  docker run --rm --init "${image}-test"
}

run_smoke_test() {
  build_runtime_image
  mkdir -p "$repo_root/runs"
  docker run --rm --init --shm-size 2g \
    --volume "$repo_root/runs:/workspace/runs" \
    "$image" mosim-smoke --steps 20
}

require_docker
case "$mode" in
  build) build_runtime_image ;;
  test) run_tests ;;
  smoke) run_tests; run_smoke_test ;;
  *)
    echo "usage: $0 [build|test|smoke]" >&2
    exit 2
    ;;
esac
