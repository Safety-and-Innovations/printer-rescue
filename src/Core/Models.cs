namespace PrinterRescue.Core;

/// <summary>Target printer, as seen by the system.</summary>
public sealed record PrinterTarget(
    string Name,
    string? ShareName,
    string? PortName,
    PrinterProtocol Protocol,
    string? DeviceId,
    string? DriverName,
    string? DriverVersion);

/// <summary>Print port configuration.</summary>
public sealed record PortConfig(
    string PortName,
    string HostAddress,
    int PortNumber,
    PrinterProtocol Protocol);

/// <summary>Print queue state.</summary>
public sealed record QueueState(
    string Name,
    bool Exists,
    int StuckJobs,
    string? DefaultPaperSize,
    int CopiesDefault,
    bool ColorDefault,
    bool DuplexDefault);

/// <summary>Driver used by the printer.</summary>
public sealed record DriverInfo(
    string Name,
    string Version,
    string? InfName,
    bool PresentInDriverStore,
    bool IsIppClassDriver);

/// <summary>Complete functional state of a printer at an instant — the product.</summary>
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

/// <summary>Snapshot summary for listing.</summary>
public sealed record SnapshotSummary(
    Guid Id,
    DateTime CreatedAtUtc,
    SnapshotOrigin Origin,
    string PrinterName);

/// <summary>Outcome of an individual check.</summary>
public sealed record CheckOutcome(
    CheckId Id,
    CheckResult Result,
    Severity Severity,
    string Detail);

/// <summary>Planned repair step.</summary>
public sealed record RepairStep(
    RepairActionKind Kind,
    string Description,
    bool Destructive,
    bool RequiresElevation);

/// <summary>Ordered repair plan for a target.</summary>
public sealed record RepairPlan(
    Guid TargetId,
    IReadOnlyList<RepairStep> Steps,
    bool RequiresElevation);

/// <summary>Diagnostic report with the attached plan.</summary>
public sealed record DiagnosticReport(
    Guid TargetId,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc,
    IReadOnlyList<CheckOutcome> Checks,
    RepairPlan Plan);

/// <summary>Outcome of running a repair step.</summary>
public sealed record RepairOutcome(
    Guid TargetId,
    Guid SnapshotId,
    RepairActionKind Kind,
    RepairStatus Status,
    string Detail);
