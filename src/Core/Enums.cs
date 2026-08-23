namespace PrinterRescue.Core;

/// <summary>Protocolo de comunicação com a impressora.</summary>
public enum PrinterProtocol
{
    TcpRaw,
    Lpr,
    Ipp,
    Usb,
    Wsd,
}

/// <summary>Identificador das verificações do diagnóstico determinístico (ordem de cima para baixo).</summary>
public enum CheckId
{
    SpoolerRunning,
    PortOpen,
    DriverPresent,
    QueueExists,
    QueueNotStuck,
    NoDuplicateInstall,
}

/// <summary>Resultado individual de um check.</summary>
public enum CheckResult
{
    Pass,
    Fail,
    Warn,
    NotApplicable,
}

/// <summary>Severidade de um achado.</summary>
public enum Severity
{
    Info,
    Warning,
    Error,
}

/// <summary>Ações de reparo suportadas.</summary>
public enum RepairActionKind
{
    RestartSpooler,
    ClearQueue,
    ReinstallWithIppClassDriver,
    ReinstallFromDriverStore,
    RemoveBrokenInstall,
    RestorePort,
    RestorePermissions,
    RestoreDefaults,
}

/// <summary>Resultado de uma ação de reparo.</summary>
public enum RepairStatus
{
    Applied,
    Failed,
    SkippedPolicyViolation,
    SkippedNoSnapshot,
}

/// <summary>Origem do snapshot.</summary>
public enum SnapshotOrigin
{
    Manual,
    PreRepair,
    PostRepair,
    Scheduled,
}
