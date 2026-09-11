#!/usr/bin/env bash
# Printer Rescue — development build (Linux/macOS)
# The production target is Windows; this script validates what compiles off Windows
# (Core + CLI + GUI ViewModels and the Core tests). win-x64 publishing happens in CI.
set -euo pipefail
cd "$(dirname "$0")"

projects=(
  src/Core/Core.csproj
  src/Cli/Cli.csproj
  src/Gui/Gui.csproj
  tests/Core.Tests/Core.Tests.csproj
)

echo "== restore =="
for p in "${projects[@]}"; do dotnet restore "$p"; done

echo "== build (zero warnings) =="
dotnet build --no-restore -c Release PrinterRescue.sln

echo "== test (Core.Tests: pure logic on any OS) =="
dotnet test --no-build -c Release tests/Core.Tests/Core.Tests.csproj

echo "OK"
