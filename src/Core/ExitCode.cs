namespace PrinterRescue.Core;

/// <summary>Exit codes of the CLI and the GUI-to-engine flow.</summary>
public enum ExitCode
{
    Ok = 0,
    DiagnosticFailed = 2,
    RepairApplied = 4,
    RepairBlockedByPolicy = 6,
    SnapshotMissing = 8,
    InvalidArguments = 16,
    UnhandledError = 32,
}
