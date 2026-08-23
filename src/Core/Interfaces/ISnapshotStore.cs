namespace PrinterRescue.Core.Interfaces;

/// <summary>Persistência e recuperação de snapshots.</summary>
public interface ISnapshotStore
{
    Task SaveAsync(global::PrinterRescue.Core.PrinterSnapshot snapshot, CancellationToken ct = default);

    Task<global::PrinterRescue.Core.PrinterSnapshot?> FindLatestForAsync(string printerName, CancellationToken ct = default);

    Task<IReadOnlyList<global::PrinterRescue.Core.SnapshotSummary>> ListAsync(CancellationToken ct = default);

    Task<global::PrinterRescue.Core.PrinterSnapshot?> LoadAsync(Guid id, CancellationToken ct = default);

    Task DeleteAllAsync(CancellationToken ct = default);
}
