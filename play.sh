#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
if [[ ! -x Build/Linux/OrbitalInvaders.x86_64 ]]; then echo "Build missing - run ./build.sh" >&2; exit 1; fi
# Refuse to start when the GPU is nearly full: running out of VRAM on NVIDIA
# takes Hyprland down with the game (crashed the desktop on 2026-10-16).
MIN_FREE_MIB="${MIN_FREE_MIB:-1500}"
if command -v nvidia-smi >/dev/null; then
  free=$(nvidia-smi --query-gpu=memory.free --format=csv,noheader,nounits | head -1 | tr -d ' ')
  if [[ "$free" =~ ^[0-9]+$ ]] && (( free < MIN_FREE_MIB )); then
    msg="Only ${free} MiB GPU memory free (need ${MIN_FREE_MIB}). Close GPU-heavy apps first (e.g. systemctl --user stop shona-translate)."
    echo "$msg" >&2
    nvidia-smi --query-compute-apps=pid,process_name,used_memory --format=csv,noheader >&2
    # launched from the app menu there is no terminal, so say it on the desktop too
    command -v notify-send >/dev/null && notify-send -a "Orbital Invaders" -i "$PWD/icon.png" "Orbital Invaders can't start" "$msg"
    exit 2
  fi
fi
mkdir -p Logs
exec ./Build/Linux/OrbitalInvaders.x86_64 -logFile "$PWD/Logs/player.log" "$@"
