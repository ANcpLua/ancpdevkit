#!/usr/bin/env bash
set -euo pipefail

echo "Building tools (Release)..."
dotnet build tools/ancp/f/f.fsproj -c Release -v minimal
dotnet build tools/ancp/fc/fc.fsproj -c Release -v minimal

echo "Installing/updating global tools..."
dotnet tool install --global --add-source ./tools/ancp/f/nupkg ancp \
  || dotnet tool update --global --add-source ./tools/ancp/f/nupkg ancp

dotnet tool install --global --add-source ./tools/ancp/fc/nupkg --prerelease ancp.fc \
  || dotnet tool update --global --add-source ./tools/ancp/fc/nupkg --prerelease ancp.fc

echo "Done. Commands available: f, fc"
