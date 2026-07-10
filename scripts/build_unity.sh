#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$repo_root/scripts/preflight.sh"
mode="${1:-all}"

if [[ -n "${UNITY_EDITOR:-}" ]]; then
  editor="$UNITY_EDITOR"
else
  editor="$(find "$HOME/Unity/Hub/Editor" "$HOME/.local/share/unity3d/Unity/Hub/Editor" \
    -path '*/2023.2.22f1/Editor/Unity' -type f -print -quit 2>/dev/null || true)"
fi

if [[ -z "$editor" || ! -x "$editor" ]]; then
  echo "Unity 2023.2.22f1 was not found. Set UNITY_EDITOR or run scripts/install_unity.sh." >&2
  exit 1
fi

mkdir -p "$repo_root/_Build/RL"

build_server() {
  "$editor" -batchmode -quit -projectPath "$repo_root" \
    -executeMethod MoSimRlBuild.BuildLinuxServer \
    -logFile "$repo_root/_Build/RL/build-server.log"
}

build_development() {
  "$editor" -batchmode -quit -projectPath "$repo_root" \
    -executeMethod MoSimRlBuild.BuildLinuxDevelopment \
    -logFile "$repo_root/_Build/RL/build-development.log"
}

case "$mode" in
  server) build_server ;;
  development) build_development ;;
  all) build_server; build_development ;;
  *) echo "usage: $0 [server|development|all]" >&2; exit 2 ;;
esac
