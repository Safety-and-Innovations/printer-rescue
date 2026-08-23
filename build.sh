#!/usr/bin/env bash
# Printer Rescue — build de desenvolvimento (Linux/macOS)
# O alvo de produção é Windows; este script valida o que compila fora do Windows
# (Core + CLI + ViewModels da GUI e os testes do Core). Publicação win-x64 no CI.
set -euo pipefail
cd "$(dirname "$0")"

projetos=(
  src/Core/Core.csproj
  src/Cli/Cli.csproj
  src/Gui/Gui.csproj
  tests/Core.Tests/Core.Tests.csproj
)

echo "== restore =="
for p in "${projetos[@]}"; do dotnet restore "$p"; done

echo "== build (zero warnings) =="
dotnet build --no-restore -c Release PrinterRescue.sln

echo "== test (Core.Tests: lógica pura em qualquer SO) =="
dotnet test --no-build -c Release tests/Core.Tests/Core.Tests.csproj

echo "OK"
