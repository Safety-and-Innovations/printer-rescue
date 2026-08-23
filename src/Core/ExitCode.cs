namespace PrinterRescue.Core;

/// <summary>Códigos de saída da CLI e do fluxo GUI→motor.</summary>
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
