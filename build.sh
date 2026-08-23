#!/usr/bin/env bash
# Printer Rescue — build de desenvolvimento (Linux/macOS)
# O alvo de produção é Windows; este script valida o que compila fora do Windows
# (Core + testes do Core). Publicação win-x64 fica no CI/pipeline do dono.
set -euo pipefail
cd "$(dirname "$0")"

echo "== restore =="
dotnet restore src/Core/Core.csproj src/Cli/Cli.csproj tests/Core.Tests/Core.Tests.csproj

echo "== build (zero warnings) =="
dotnet build --no-restore -c Release src/Core/Core.csproj src/Cli/Cli.csproj tests/Core.Tests/Core.Tests.csproj

echo "== test =="
dotnet test --no-build -c Release tests/Core.Tests/Core.Tests.csproj

echo "OK"
