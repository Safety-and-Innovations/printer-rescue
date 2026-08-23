namespace PrinterRescue.Core.Interfaces;

/// <summary>Executa a sequência determinística de checks e produz o relatório.</summary>
public interface IDiagnosticEngine
{
    Task<global::PrinterRescue.Core.DiagnosticReport> DiagnoseAndPlanAsync(
        global::PrinterRescue.Core.PrinterTarget target,
        CancellationToken ct = default);
}
