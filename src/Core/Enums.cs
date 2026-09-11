namespace PrinterRescue.Core;

/// <summary>Printer communication protocol.</summary>
public enum PrinterProtocol
{
    TcpRaw,
    Lpr,
    Ipp,
    Usb,
    Wsd,
}

/// <summary>Identifier of the deterministic diagnostic checks (top-to-bottom order).</summary>
public enum CheckId
{
    SpoolerRunning,
    PortOpen,
    DriverPresent,
    QueueExists,
    QueueNotStuck,
    NoDuplicateInstall,
}

/// <summary>Individual result of a check.</summary>
public enum CheckResult
{
    Pass,
    Fail,
    Warn,
    NotApplicable,
}

/// <summary>Severity of a finding.</summary>
public enum Severity
{
    Info,
    Warning,
    Error,
}

/// <summary>Supported repair actions.</summary>
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

/// <summary>Outcome of a repair action.</summary>
public enum RepairStatus
{
    Applied,
    Failed,
    SkippedPolicyViolation,
    SkippedNoSnapshot,
}

/// <summary>Snapshot origin.</summary>
public enum SnapshotOrigin
{
    Manual,
    PreRepair,
    PostRepair,
    Scheduled,
}
