#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
cd "$repo_dir"

"$dotnet_command" run --project Tests/TechTrader.Core.Tests -c Release
for game_version in 1.4 1.5 1.6; do
  "$dotnet_command" build Source/TechTrader/TechTrader.csproj -c Release -p:RimWorldVersion="$game_version"
done
python3 scripts/package.py
