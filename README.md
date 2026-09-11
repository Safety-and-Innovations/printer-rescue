# Printer Rescue

![status](https://img.shields.io/badge/status-in%20development-yellow) ![stack](https://img.shields.io/badge/.NET-8.0-blue) ![tests](https://img.shields.io/badge/TDD-xUnit-green)

> "This printer was working with IP 192.168.0.40, driver Y, port Z.
> Restore that state?"

## What it is for

The #1 ticket of any helpdesk: the printer worked yesterday and is gone today —
after a Windows Update, a network change, a DHCP lease that moved the IP,
a stuck queue. Aggravated in 2026: Microsoft stopped shipping new V3/V4 drivers
via Windows Update; old printers no longer fix themselves.

Printer Rescue records the **known-good state** of each printer
(IP, port, protocol, driver and version, queue, permissions, defaults) and rebuilds it
when Windows breaks. Generic, deterministic, with no per-model knowledge base.

### RULE #1 — written, not intended

> **Printer Rescue never hosts, distributes, indexes, or keeps any printer
> driver. It restores state; it does not ship binaries.**

Criterion: if serving a request requires adding one line per printer model
sold in the world, the answer is no. No exceptions.

| ✅ Allowed | ❌ Forbidden |
|---|---|
| Use a driver that is **already** in the DriverStore | Download a driver from our server |
| Prefer the native **IPP Class Driver** | Keep a `model → driver` table |
| Record/restore which driver was in use | "Support for HP 1102w" as a feature |
| Point to the vendor's official page | Mirror the vendor installer |

The rule is code: `IPolicyGuard` blocks at runtime any step involving
driver distribution (`SkippedPolicyViolation`).

## How it works

Deterministic top-down pipeline:

```
detect printer → test IP → test port → test spooler → test driver
→ detect duplicate → remove the broken one → reinstall (preferring IPP Class Driver)
→ print test page → record a new snapshot
```

Architecture:

```
┌─────────────┐   ┌──────────────────────────┐
│  GUI (Ava)  │   │  CLI (--json for RMM)    │   thin facades
└──────┬──────┘   └────────────┬─────────────┘
       └──────────┬────────────┘
                  ▼
        ┌─────────────────────┐
        │   PrinterRescue.Core│  diagnostics · repair plan · snapshots · policy guard
        └──────────┬──────────┘
                   ▼
        ┌─────────────────────┐
        │ Adapters.Windows    │  winspool.drv / spooler via P/Invoke with guards
        └─────────────────────┘
```

- **Cross-platform Core**: all logic runs and is tested on Linux/CI.
- **Adapters.Windows**: the only place touching the Windows API; cross-compiles,
  runs on Windows only (`OperatingSystem.IsWindows()` guards).
- **On-disk snapshot**: `%ProgramData%\PrinterRescue\snapshots\<id>.json`,
  hardened parser (depth ≤ 16, size ≤ 1 MiB, anti path traversal).

## Guarantees

- Build with `TreatWarningsAsErrors` + `latest-recommended` analyzers: zero warnings.
- Strict TDD: no production code without a failing test first.
- A destructive operation **never** runs without a prior snapshot (`SkippedNoSnapshot`).
- Driver distribution **never** happens (`SkippedPolicyViolation`) — tested.
- Core has no external dependencies (BCL only).

## Stack

C# 12 · .NET 8 · xUnit + coverlet · Avalonia 11 (GUI) · System.CommandLine (CLI)
· GitHub Actions (Windows build/test + Linux core tests)

## Current status

- [x] Solution scaffold (Core, Adapters.Windows, Cli, Gui, tests)
- [x] Frozen contracts (`docs/CONTRACTS.md` v1.0.0) materialized in code
- [x] Snapshots module — hardened JSON persistence, 12 tests
- [x] Deterministic diagnostics — engine + 6 checks, spooler gating
- [x] Repair + Policy Guard — RULE #1 encoded, contractual plan order
- [x] Workflows — repair capture and execution with pre/post snapshot
- [x] Windows adapters — full winspool.drv (gateway + executor)
- [x] Full CLI (`list`, `snapshot`, `diagnose`, `repair`, `snapshots`)
- [x] Avalonia GUI — main window with diagnose/snapshot flow

**90/90 tests green** (Core.Tests) + **18/18** (Windows.Tests, mappers).
- [x] Portable win-x64 packaging — `dist/` (gitignored) produces
      `printer-rescue-v1.0.0-win-x64-portable.zip`: published CLI, usage README,
      SHA256SUMS. Local CI green: Release build 0 warnings + 108 tests.

## What is left (roadmap)

| Milestone | Scope |
|---|---|
| M1 ✅ | Full Core with high coverage — done |
| M2 ⏳ | Validation on a real Windows machine (P/Invoke, spooler, Driver Store) |
| M3 ⏳ | End-to-end CLI integration tests against a fake gateway |
| M4 ⏳ | Full repair flow in the GUI (today: diagnose/snapshot; repair via CLI) |
| M5 ⏳ | MSIX/Store installer with policy 10.2.4 declaration |
| v2 (to decide) | Multi-machine MSP central dashboard; portable tech SKU |

## Open ideas and questions

- Per-endpoint/year pricing as the natural model for MSPs (pending decision).
- Multi-machine central dashboard: v2 or never? (that is what brings infra cost.)
- Portable tech edition as a separate SKU?

## Development

```bash
# Linux/macOS (validates Core + Core tests)
./build.sh

# Full (requires Windows for adapters/GUI)
dotnet build PrinterRescue.sln -c Release
dotnet test  PrinterRescue.sln -c Release
```

---

Created by forg3 · MIT License
