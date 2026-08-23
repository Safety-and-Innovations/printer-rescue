namespace PrinterRescue.Core.Interfaces;

/// <summary>Executa ações no sistema. Implementação Windows vive nos adapters.</summary>
public interface IRepairExecutor
{
    bool IsElevated();

    Task<global::PrinterRescue.Core.RepairOutcome> ExecuteAsync(
        global::PrinterRescue.Core.RepairStep action,
        global::PrinterRescue.Core.PrinterSnapshot context,
        CancellationToken ct = default);
}
