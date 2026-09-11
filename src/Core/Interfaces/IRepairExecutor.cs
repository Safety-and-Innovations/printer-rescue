namespace PrinterRescue.Core.Interfaces;

/// <summary>Runs actions against the system. The Windows implementation lives in the adapters.</summary>
public interface IRepairExecutor
{
    bool IsElevated();

    Task<global::PrinterRescue.Core.RepairOutcome> ExecuteAsync(
        global::PrinterRescue.Core.RepairStep action,
        global::PrinterRescue.Core.PrinterSnapshot context,
        CancellationToken ct = default);
}
