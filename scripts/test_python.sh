#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

# ROS installations commonly expose incompatible global pytest plugins through
# PYTHONPATH. The project test suite intentionally runs only its declared plugins.
PYTEST_DISABLE_PLUGIN_AUTOLOAD=1 conda run --name mosim-rl \
  pytest python/tests -m "${MOSIM_PYTEST_MARKS:-not integration}"
