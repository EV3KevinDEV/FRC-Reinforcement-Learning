#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$repo_root/scripts/preflight.sh"

version="2023.2.22f1"
changeset="6b19bf4f8115"
hub_root="${UNITY_HUB_ROOT:-$HOME/.local/opt/unityhub}"
mkdir -p "$hub_root"

if command -v unityhub >/dev/null 2>&1; then
  run_hub() {
    env -u ELECTRON_RUN_AS_NODE unityhub -- --headless "$@"
  }
else
  if ! command -v bwrap >/dev/null 2>&1; then
    echo "A system Unity Hub install or bubblewrap is required for user-local setup." >&2
    exit 1
  fi

  packages_file="$hub_root/Packages"
  curl --fail --silent --show-error --location \
    https://hub.unity3d.com/linux/repos/deb/dists/stable/main/binary-amd64/Packages.gz \
    | gzip -dc > "$packages_file"
  package_path="$(awk '/^Filename:/{path=$2} END{print path}' "$packages_file")"
  package_sha="$(awk '/^SHA256:/{sha=$2} END{print sha}' "$packages_file")"
  hub_deb="$hub_root/UnityHubSetup.deb"
  if [[ ! -f "$hub_deb" ]] || ! echo "$package_sha  $hub_deb" | sha256sum --check --status; then
    echo "Downloading the official Unity Hub Debian package..."
    curl --fail --location --retry 5 --output "$hub_deb" \
      "https://hub.unity3d.com/linux/repos/deb/$package_path"
  fi
  echo "$package_sha  $hub_deb" | sha256sum --check --status
  rm -rf "$hub_root/root"
  mkdir -p "$hub_root/root"
  dpkg-deb --extract "$hub_deb" "$hub_root/root"
  extracted_hub="$hub_root/root/opt/unityhub"

  run_hub() {
    env -u ELECTRON_RUN_AS_NODE bwrap \
      --dev-bind / / \
      --tmpfs /opt \
      --dir /opt/unityhub \
      --bind "$extracted_hub" /opt/unityhub \
      /opt/unityhub/unityhub -- --headless "$@"
  }
fi

echo "Unity Hub must be signed in and have an active license before the editor can build."
mkdir -p "$HOME/Unity/Hub/Editor"
run_hub install-path -s "$HOME/Unity/Hub/Editor"
run_hub install \
  --version "$version" \
  --changeset "$changeset" \
  --module linux-server
