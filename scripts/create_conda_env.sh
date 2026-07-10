#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
"$repo_root/scripts/preflight.sh"

if ! conda env list | awk '{print $1}' | grep -qx mosim-rl; then
  conda create --name mosim-rl --solver=libmamba --yes python=3.11 pip
fi
env_python="$(conda run --name mosim-rl which python | tail -n 1)"

if command -v uv >/dev/null 2>&1; then
  UV_HTTP_TIMEOUT=600 UV_HTTP_RETRIES=10 uv pip install \
    --python "$env_python" -e 'python[test]'
else
  conda run --name mosim-rl python -m pip install --timeout 600 -e 'python[test]'
fi

conda list --name mosim-rl --explicit > python/conda-linux-64.lock
env -u PYTHONPATH conda env export --name mosim-rl --no-builds \
  | sed '/^prefix: /d' > python/environment.resolved.yml
if command -v uv >/dev/null 2>&1; then
  uv pip compile python/pyproject.toml --extra test --python-version 3.11 \
    --output-file python/requirements.lock
else
  env -u PYTHONPATH conda run --name mosim-rl python -m pip freeze --all \
    > python/requirements.lock
fi
conda run --name mosim-rl python -c \
  'import gymnasium, stable_baselines3, torch; print(gymnasium.__version__, stable_baselines3.__version__, torch.__version__)'
