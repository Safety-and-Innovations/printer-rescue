namespace PrinterRescue.Core.Interfaces;

/// <summary>Runs the deterministic check sequence and produces the report.</summary>
public interface IDiagnosticEngine
{
    Task<global::PrinterRescue.Core.DiagnosticReport> DiagnoseAndPlanAsync(
        global::PrinterRescue.Core.PrinterTarget target,
        CancellationToken ct = default);
}
