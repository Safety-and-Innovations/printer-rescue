# Printer Rescue

![status](https://img.shields.io/badge/status-em%20desenvolvimento-yellow) ![stack](https://img.shields.io/badge/.NET-8.0-blue) ![tests](https://img.shields.io/badge/TDD-xUnit-green)

> "Esta impressora estava funcionando com IP 192.168.0.40, driver Y, porta Z.
> Restaurar esse estado?"

## Para que serve

O ticket nº 1 de qualquer helpdesk: a impressora funcionava ontem e sumiu hoje —
depois de um Windows Update, de uma troca de rede, de um DHCP que mudou o IP,
de uma fila travada. Agravante de 2026: a Microsoft parou de distribuir drivers
V3/V4 novos via Windows Update; impressoras antigas não se resolvem mais sozinhas.

O Printer Rescue grava o **estado funcional conhecido** de cada impressora
(IP, porta, protocolo, driver e versão, fila, permissões, padrões) e o reconstrói
quando o Windows quebra. Genérico, determinístico, sem base de conhecimento por modelo.

### REGRA Nº 1 — escrita, não intencional

> **O Printer Rescue nunca hospeda, distribui, indexa ou mantém driver de
> impressora. Ele restaura estado; ele não fornece binário.**

Critério: se atender a um pedido exige acrescentar uma linha por modelo de
impressora vendido no mundo, a resposta é não. Sem exceção.

| ✅ Permitido | ❌ Proibido |
|---|---|
| Usar driver que **já está** no DriverStore | Baixar driver de servidor nosso |
| Preferir **IPP Class Driver** (nativo) | Manter tabela `modelo → driver` |
| Gravar/restaurar qual driver estava em uso | "Suporte à HP 1102w" como feature |
| Apontar à página oficial do fabricante | Espelhar instalador do fabricante |

A regra é código: `IPolicyGuard` bloqueia em runtime qualquer passo que envolva
distribuição de driver (`SkippedPolicyViolation`).

## Como funciona

Pipeline determinístico de cima para baixo:

```
detecta impressora → testa IP → testa porta → testa spooler → testa driver
→ detecta duplicata → remove a quebrada → reinstala (preferindo IPP Class Driver)
→ imprime página de teste → grava novo snapshot
```

Arquitetura:

```
┌─────────────┐   ┌──────────────────────────┐
│  GUI (Ava)  │   │  CLI (--json p/ RMM)     │   fachadas finas
└──────┬──────┘   └────────────┬─────────────┘
       └──────────┬────────────┘
                  ▼
        ┌─────────────────────┐
        │   PrinterRescue.Core│  diagnóstico · plano de reparo · snapshots · policy guard
        └──────────┬──────────┘
                   ▼
        ┌─────────────────────┐
        │ Adapters.Windows    │  winspool.drv / spooler via P/Invoke com guards
        └─────────────────────┘
```

- **Core multiplataforma**: toda a lógica roda e é testada em Linux/CI.
- **Adapters.Windows**: único ponto que toca API do Windows; compila cross-plataforma,
  executa só em Windows (guards `OperatingSystem.IsWindows()`).
- **Snapshot em disco**: `%ProgramData%\PrinterRescue\snapshots\<id>.json`,
  parser endurecido (profundidade ≤ 16, tamanho ≤ 1 MiB, anti path traversal).

## Garantias

- Build com `TreatWarningsAsErrors` + analyzers `latest-recommended`: zero warnings.
- TDD estrito: nenhum código de produção sem teste falho antes.
- Operação destrutiva **nunca** ocorre sem snapshot prévio (`SkippedNoSnapshot`).
- Distribuição de driver **nunca** ocorre (`SkippedPolicyViolation`) — testado.
- Core sem dependência externa (apenas BCL).

## Stack

C# 12 · .NET 8 · xUnit + coverlet · Avalonia 11 (GUI) · System.CommandLine (CLI)
· GitHub Actions (Windows build/test + Linux core tests)

## Estado atual

- [x] Scaffold da solução (Core, Adapters.Windows, Cli, Gui, testes)
- [x] Contratos congelados (`docs/CONTRATOS.md` v1.0.0) e materializados em código
- [x] Módulo de snapshots — persistência JSON endurecida, 12 testes
- [x] Diagnóstico determinístico — engine + 6 checks, gating por spooler
- [x] Reparo + Policy Guard — REGRA Nº 1 codificada, ordem contratual do plano
- [x] Workflows — captura e execução de reparo com snapshot pré/pós
- [x] Adapters Windows — winspool.drv completo (gateway + executor)
- [x] CLI completa (`list`, `snapshot`, `diagnose`, `repair`, `snapshots`)
- [x] GUI Avalonia — janela principal com fluxo diagnosticar/snapshot

**90/90 testes verdes** (Core.Tests) + **18/18** (Windows.Tests, mapeadores).
- [x] Empacotamento portable win-x64 — `dist/` (gitignored) gera
      `printer-rescue-v1.0.0-win-x64-portable.zip`: CLI publicada, README de uso,
      SHA256SUMS. CI local verde: build Release 0 warnings + 108 testes.

## O que falta (roadmap)

| Marco | Escopo |
|---|---|
| M1 ✅ | Core completo com cobertura alta — entregue |
| M2 ⏳ | Validação em máquina Windows real (P/Invoke, spooler, Driver Store) |
| M3 ⏳ | Testes de integração da CLI contra gateway falso end-to-end |
| M4 ⏳ | Fluxo de reparo completo na GUI (hoje: diagnosticar/snapshot; reparo via CLI) |
| M5 ⏳ | Instalador MSIX/Store com declaração da política 10.2.4 |
| v2 (decidir) | Painel central MSP multi-máquina; SKU portátil para técnico |

## Ideias e questões abertas

- Preço por endpoint/ano como modelo natural para MSP (decisão pendente).
- Painel central multi-máquina: v2 ou nunca? (é o que traz custo de infra.)
- Versão portátil para técnico como SKU separado?

## Desenvolvimento

```bash
# Linux/macOS (valida Core + testes do Core)
./build.sh

# Completo (requer Windows para os adapters/GUI)
dotnet build PrinterRescue.sln -c Release
dotnet test  PrinterRescue.sln -c Release
```

---

Criado por André Santo (forg3) | junkyardgoodies.app
