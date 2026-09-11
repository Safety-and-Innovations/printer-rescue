using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Tests;

/// <summary>Fake check: returns a pre-defined outcome without touching the gateway.</summary>
public sealed class StubCheck : IDiagnosticCheck
{
    private readonly Func<PrinterTarget, CheckOutcome> _run;

    public StubCheck(CheckId id, CheckOutcome outcome)
        : this(id, _ => outcome)
    {
    }

    public StubCheck(CheckId id, Func<PrinterTarget, CheckOutcome> run)
    {
        Id = id;
        _run = run;
    }

    public CheckId Id { get; }

    public Task<CheckOutcome> RunAsync(IPrintSystemGateway gateway, PrinterTarget target, CancellationToken ct = default)
        => Task.FromResult(_run(target));
}

/// <summary>Fake check that always throws — validates the engine's handling.</summary>
public sealed class ThrowingCheck : IDiagnosticCheck
{
    public ThrowingCheck(CheckId id) => Id = id;

    public CheckId Id { get; }

    public Task<CheckOutcome> RunAsync(IPrintSystemGateway gateway, PrinterTarget target, CancellationToken ct = default)
        => throw new InvalidOperationException("boom");
}

/// <summary>Fake planner that records the arguments received from the engine.</summary>
public sealed class FakeRepairPlanner : IRepairPlanner
{
    private RepairPlan? _returnPlan;

    public int Calls { get; private set; }

    public DiagnosticReport? ReceivedReport { get; private set; }

    public PrinterSnapshot? LastGood { get; private set; }

    /// <summary>When set, this is the plan returned by PlanRepairs.</summary>
    public RepairPlan? ReturnPlan { get => _returnPlan; init => _returnPlan = value; }

    public RepairPlan PlanRepairs(DiagnosticReport report, PrinterSnapshot? lastGood)
    {
        Calls++;
        ReceivedReport = report;
        LastGood = lastGood;
        return _returnPlan ?? new RepairPlan(report.TargetId, [], false);
    }
}

/// <summary>
/// Fake snapshot store that records the queried name — distinct from FakeSnapshotStore
/// so engine tests are not coupled to the other double's behavior.
/// </summary>
public sealed class RecordingSnapshotStore : ISnapshotStore
{
    private readonly PrinterSnapshot? _latest;

    public RecordingSnapshotStore(PrinterSnapshot? latest = null) => _latest = latest;

    public string? QueriedName { get; private set; }

    public Task SaveAsync(PrinterSnapshot snapshot, CancellationToken ct = default) => Task.CompletedTask;

    public Task<PrinterSnapshot?> FindLatestForAsync(string printerName, CancellationToken ct = default)
    {
        QueriedName = printerName;
        return Task.FromResult(_latest);
    }

    public Task<IReadOnlyList<SnapshotSummary>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SnapshotSummary>>([]);

    public Task<PrinterSnapshot?> LoadAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<PrinterSnapshot?>(null);

    public Task DeleteAllAsync(CancellationToken ct = default) => Task.CompletedTask;
}
