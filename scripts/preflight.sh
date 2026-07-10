#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
minimum_gib="${MOSIM_MIN_FREE_GIB:-25}"
available_kib="$(df -Pk "$repo_root" | awk 'NR == 2 {print $4}')"
required_kib="$((minimum_gib * 1024 * 1024))"

if (( available_kib < required_kib )); then
  available_gib="$((available_kib / 1024 / 1024))"
  echo "MoSimulator setup requires at least ${minimum_gib} GiB free; ${available_gib} GiB is available." >&2
  exit 1
fi

echo "Preflight passed: $((available_kib / 1024 / 1024)) GiB free at $repo_root"
