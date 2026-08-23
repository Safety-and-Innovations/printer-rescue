using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Tests;

/// <summary>Loja de snapshots falsa para testes de engine/planner: comportamento configurável.</summary>
public sealed class FakeSnapshotStore : ISnapshotStore
{
    private readonly Dictionary<Guid, PrinterSnapshot> _porId = new();
    private readonly PrinterSnapshot? _lastGood;

    public FakeSnapshotStore(PrinterSnapshot? lastGood = null)
    {
        if (lastGood is not null)
        {
            _lastGood = lastGood;
            _porId[lastGood.Id] = lastGood;
        }
    }

    public int SaveCalls { get; private set; }

    public Task SaveAsync(PrinterSnapshot snapshot, CancellationToken ct = default)
    {
        SaveCalls++;
        _porId[snapshot.Id] = snapshot;
        return Task.CompletedTask;
    }

    public Task<PrinterSnapshot?> LoadAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_porId.TryGetValue(id, out var snap) ? snap : null);

    public Task<PrinterSnapshot?> FindLatestForAsync(string printerName, CancellationToken ct = default)
        => Task.FromResult(_lastGood);

    public Task<IReadOnlyList<SnapshotSummary>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SnapshotSummary>>(
            _porId.Values
                .OrderByDescending(static s => s.CreatedAtUtc)
                .Select(static s => new SnapshotSummary(s.Id, s.CreatedAtUtc, s.Origin, s.Target.Name))
                .ToList());

    public Task DeleteAllAsync(CancellationToken ct = default)
    {
        _porId.Clear();
        return Task.CompletedTask;
    }
}
