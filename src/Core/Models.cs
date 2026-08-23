namespace PrinterRescue.Core;

/// <summary>Impressora alvo, como vista pelo sistema.</summary>
public sealed record PrinterTarget(
    string Name,
    string? ShareName,
    string? PortName,
    PrinterProtocol Protocol,
    string? DeviceId,
    string? DriverName,
    string? DriverVersion);

/// <summary>Configuração de porta de impressão.</summary>
public sealed record PortConfig(
    string PortName,
    string HostAddress,
    int PortNumber,
    PrinterProtocol Protocol);

/// <summary>Estado da fila de impressão.</summary>
public sealed record QueueState(
    string Name,
    bool Exists,
    int StuckJobs,
    string? DefaultPaperSize,
    int CopiesDefault,
    bool ColorDefault,
    bool DuplexDefault);

/// <summary>Driver em uso pela impressora.</summary>
public sealed record DriverInfo(
    string Name,
    string Version,
    string? InfName,
    bool PresentInDriverStore,
    bool IsIppClassDriver);

/// <summary>Estado funcional completo de uma impressora em um instante — o produto.</summary>
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

/// <summary>Resumo de snapshot para listagem.</summary>
public sealed record SnapshotSummary(
    Guid Id,
    DateTime CreatedAtUtc,
    SnapshotOrigin Origin,
    string PrinterName);

/// <summary>Resultado de um check individual.</summary>
public sealed record CheckOutcome(
    CheckId Id,
    CheckResult Result,
    Severity Severity,
    string Detail);

/// <summary>Passo de reparo planejado.</summary>
public sealed record RepairStep(
    RepairActionKind Kind,
    string Description,
    bool Destructive,
    bool RequiresElevation);

/// <summary>Plano de reparo ordenado para um alvo.</summary>
public sealed record RepairPlan(
    Guid TargetId,
    IReadOnlyList<RepairStep> Steps,
    bool RequiresElevation);

/// <summary>Relatório de diagnóstico com plano anexo.</summary>
public sealed record DiagnosticReport(
    Guid TargetId,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc,
    IReadOnlyList<CheckOutcome> Checks,
    RepairPlan Plan);

/// <summary>Resultado da execução de um passo de reparo.</summary>
public sealed record RepairOutcome(
    Guid TargetId,
    Guid SnapshotId,
    RepairActionKind Kind,
    RepairStatus Status,
    string Detail);
