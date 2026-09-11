# Printer Rescue — Shared Contracts (CONTRACTS.md)

> **Version 1.0.0 · FROZEN** for the parallel implementation round.
> Any change here requires syncing with all active agents.
> Author: forg3

This document defines the shared types, enums, and interfaces across the
Printer Rescue modules. Agents must implement exactly these
names, namespaces, and signatures, or risk merge conflicts.

---

## 1. Namespaces

| Project | Root namespace |
|---|---|
| Core | `PrinterRescue.Core` |
| Adapters.Windows | `PrinterRescue.Adapters.Windows` |
| Cli | `PrinterRescue.Cli` |
| Gui | `PrinterRescue.Gui` |

## 2. Enums (namespace `PrinterRescue.Core`)

```csharp
public enum PrinterProtocol { TcpRaw, Lpr, Ipp, Usb, Wsd }

// Deterministic top-down diagnostics (idea README §Flow)
public enum CheckId
{
    SpoolerRunning,
    PortOpen,
    DriverPresent,
    QueueExists,
    QueueNotStuck,
    NoDuplicateInstall
}

public enum CheckResult { Pass, Fail, Warn, NotApplicable }

public enum Severity { Info, Warning, Error }

public enum RepairActionKind
{
    RestartSpooler,
    ClearQueue,
    ReinstallWithIppClassDriver,
    ReinstallFromDriverStore,
    RemoveBrokenInstall,
    RestorePort,
    RestorePermissions,
    RestoreDefaults
}

public enum RepairStatus { Applied, Failed, SkippedPolicyViolation, SkippedNoSnapshot }

public enum SnapshotOrigin { Manual, PreRepair, PostRepair, Scheduled }

public enum ExitCode
{
    Ok = 0,
    DiagnosticFailed = 2,
    RepairApplied = 4,
    RepairBlockedByPolicy = 6,
    SnapshotMissing = 8,
    InvalidArguments = 16,
    UnhandledError = 32
}
```

## 3. Records (namespace `PrinterRescue.Core`)

```csharp
public sealed record PrinterTarget(
    string Name,
    string? ShareName,
    string? PortName,
    PrinterProtocol Protocol,
    string? DeviceId,
    string? DriverName,
    string? DriverVersion);

public sealed record PortConfig(
    string PortName,
    string HostAddress,
    int PortNumber,
    PrinterProtocol Protocol);

public sealed record QueueState(
    string Name,
    bool Exists,
    int StuckJobs,
    string? DefaultPaperSize,
    int CopiesDefault,
    bool ColorDefault,
    bool DuplexDefault);

public sealed record DriverInfo(
    string Name,
    string Version,
    string? InfName,
    bool PresentInDriverStore,
    bool IsIppClassDriver);

public sealed record PrinterSnapshot(
    Guid Id,
    DateTime CreatedAtUtc,
    SnapshotOrigin Origin,
    PrinterTarget Target,
    PortConfig? Port,
    QueueState? Queue,
    DriverInfo? Driver,
    IReadOnlyDictionary<string, string> Permissions,
    IReadOnlyDictionary<string, string> Defaults,
    string SchemaVersion);

public sealed record SnapshotSummary(
    Guid Id,
    DateTime CreatedAtUtc,
    SnapshotOrigin Origin,
    string PrinterName);

public sealed record CheckOutcome(
    CheckId Id,
    CheckResult Result,
    Severity Severity,
    string Detail);

public sealed record RepairStep(
    RepairActionKind Kind,
    string Description,
    bool Destructive,
    bool RequiresElevation);

public sealed record RepairPlan(
    Guid TargetId,
    IReadOnlyList<RepairStep> Steps,
    bool RequiresElevation);

public sealed record RepairOutcome(
    Guid TargetId,
    Guid SnapshotId,
    RepairActionKind Kind,
    RepairStatus Status,
    string Detail);
```

## 4. Interfaces (namespace `PrinterRescue.Core.Interfaces`)

```csharp
/// <summary>Access to the real print subsystem state.</summary>
public interface IPrintSystemGateway
{
    Task<IReadOnlyList<PrinterTarget>> ListPrintersAsync(CancellationToken ct = default);
    Task<PortConfig?> GetPortAsync(string portName, CancellationToken ct = default);
    Task<QueueState> GetQueueStateAsync(string queueName, CancellationToken ct = default);
    Task<DriverInfo?> GetDriverInfoAsync(string driverName, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetPermissionsSddlAsync(string queueName, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetDefaultsAsync(string queueName, CancellationToken ct = default);
}

/// <summary>Captures and persists snapshots.</summary>
public interface ISnapshotStore
{
    Task SaveAsync(PrinterSnapshot snapshot, CancellationToken ct = default);
    Task<PrinterSnapshot?> FindLatestForAsync(string printerName, CancellationToken ct = default);
    Task<IReadOnlyList<SnapshotSummary>> ListAsync(CancellationToken ct = default);
    Task<PrinterSnapshot?> LoadAsync(Guid id, CancellationToken ct = default);
    Task DeleteAllAsync(CancellationToken ct = default);
}

/// <summary>Executes actions on Windows.</summary>
public interface IRepairExecutor
{
    bool IsElevated();
    Task<RepairOutcome> ExecuteAsync(RepairStep step, PrinterSnapshot context, CancellationToken ct = default);
}

/// <summary>Single deterministic check.</summary>
public interface IDiagnosticCheck
{
    CheckId Id { get; }
    Task<CheckOutcome> RunAsync(IPrintSystemGateway gateway, PrinterTarget target, CancellationToken ct = default);
}

public interface IDiagnosticEngine
{
    Task<DiagnosticReport> DiagnoseAndPlanAsync(PrinterTarget target, CancellationToken ct = default);
}

public interface IRepairPlanner
{
    RepairPlan PlanRepairs(DiagnosticReport report, PrinterSnapshot? lastGood);
}

public sealed record PolicyDecision(bool Allowed, string Reason);

/// <summary>Compliance guard: blocks driver distribution (RULE #1).</summary>
public interface IPolicyGuard
{
    PolicyDecision Evaluate(RepairStep step, PrinterSnapshot context);
}
```

### Check semantics (behavioral contract)

- `SpoolerRunning`: Pass if the spooler service is reachable/running; Fail otherwise; remaining checks become `NotApplicable` when this fails.
- `PortOpen`: Pass if the TCP port opens within ≤ 2 s; Fail on timeout/refusal; `NotApplicable` for USB/WSD.
- `DriverPresent`: Pass if the driver is present on the system or DriverStore.
- `QueueExists`: Pass if the queue exists and is configured.
- `QueueNotStuck`: Warn with N stuck jobs; Pass with a free queue.
- `NoDuplicateInstall`: Fail when more than one install of the same printer exists.

### `IRepairPlanner` semantics

- Input: report with ≥ 1 failed check + last valid snapshot of the printer.
- Plan order: clear stuck queue → restart spooler → restore port/queue → reinstall driver (IPP Class Driver preferred) → remove broken install.
- Without snapshot: plan only with non-destructive steps; destructive steps yield `SkippedNoSnapshot`.

## 5. On-disk snapshot format

Path: `%ProgramData%\PrinterRescue\snapshots\<id>.json` — one file per snapshot, UTF-8.

Example:

```json
{
  "schemaVersion": "1.0",
  "id": "0f8b6c1e-5e2a-4a7b-9c3d-1f2e3a4b5c6d",
  "createdAtUtc": "2026-08-23T12:00:00Z",
  "origin": "pre-repair",
  "target": {
    "name": "HP LaserJet Pro",
    "shareName": null,
    "portName": "IP_192.168.0.40",
    "protocol": "tcp_raw",
    "deviceId": null,
    "driverName": "HP Universal PCL6",
    "driverVersion": "3.12.0.0"
  },
  "port": {
    "portName": "IP_192.168.0.40",
    "hostAddress": "192.168.0.40",
    "portNumber": 9100,
    "protocol": "tcp_raw"
  },
  "queue": {
    "name": "HP LaserJet Pro",
    "exists": true,
    "stuckJobs": 0,
    "defaultPaperSize": "A4",
    "copiesDefault": 1,
    "colorDefault": false,
    "duplexDefault": true
  },
  "driver": {
    "name": "HP Universal PCL6",
    "version": "3.12.0.0",
    "infName": "hpcu270u.inf",
    "presentInDriverStore": true,
    "isIppClassDriver": false
  },
  "permissions": { "queueSddl": "G:SYD:PAI(A;;FA;;;BA)" },
  "defaults": { "paperSize": "A4", "copies": "1", "color": "false", "duplex": "true" }
}
```

Serialization rules:

- camelCase JSON, `JsonSerializerOptions` with `WriteIndented = true`.
- `protocol` enum serializes as `tcp_raw`, `lpr`, `ipp`, `usb`, `wsd` (JsonStringEnumConverter with snake_case naming).
- `origin` enum serializes as `manual`, `pre-repair`, `post-repair`, `scheduled` — use JsonPropertyName on members.
- Minimum required fields: `schemaVersion`, `id`, `createdAtUtc`, `origin`, `target.name`, `target.protocol`.
- Deserialization tolerant of missing optional fields (everything outside required is nullable).
- Read size limit: reject files > 1 MiB; max depth 16 (defense against malicious JSON).

## 6. Cross-cutting rules (mandatory for all agents)

1. **Strict TDD**: no code without a failing test first (RED→GREEN→REFACTOR), vertical slices.
2. **`TreatWarningsAsErrors`**: build with zero warnings.
3. **Async with `CancellationToken`** on all I/O; no `async void`, no `.Result`/`.Wait()`, no `Thread.Sleep` in production.
4. **Constructor dependency injection**; interfaces in Core; adapters inject gateways.
5. **No Windows-specific code in Core** — Core runs on Linux/macOS/Windows and Core tests run on this host's Linux.
6. **RULE #1 is code**: `IPolicyGuard` blocks any step involving driver download/hosting/distribution. Mandatory test: external-driver install step → `SkippedPolicyViolation`.
7. **Destructive requires prior snapshot**: without snapshot, destructive steps become `SkippedNoSnapshot`. Mandatory test.
8. **Security**: no secrets in code; traversal-validated paths; size/depth limits on snapshot parser; no PII in logs beyond what is technically needed (queue name/IP are needed).
9. **Small descriptive commits** on your own branch, one commit per finished RED→GREEN cycle.
10. **Zero external dependencies in Core** (BCL only). Adapters: BCL + locally declared P/Invoke. CLI: System.CommandLine. GUI: Avalonia.
11. **Localization**: user-facing strings in English; identifiers/code in English.

## 7. Agent limits

- Edit only `src/<your-project>/` and `tests/<YourProject>.Tests/`.
- Do not edit `Directory.Build.props`, `Directory.Build.targets`, solution, CI, README, LICENSE, docs/.
- Do not create new projects or change package versions.
- Do not implement a per-model driver catalog/database — RULE #1.
