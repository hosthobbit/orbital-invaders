#!/usr/bin/env bash
# Regenerates all assets + scene and builds the Linux player.
# -nographics: the editor never opens a GPU device, so building cannot eat VRAM.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
mkdir -p Logs
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/6000.6.3f1/Editor/Unity}"
exec "$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Orbital.EditorTools.OrbitalBuilder.GenerateAndBuild -quit -logFile "$PWD/Logs/build.log"
