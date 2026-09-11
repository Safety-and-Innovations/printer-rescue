using PrinterRescue.Core.Interfaces;
using PrinterRescue.Core.Snapshots;
using PrinterRescue.Core.Workflows;
using PrinterRescue.Core.Tests.Snapshots;
using Xunit;

namespace PrinterRescue.Core.Tests.Workflows;

/// <summary>Fake executor that records received steps and returns a configurable result.</summary>
public sealed class FakeExecutor : IRepairExecutor
{
    public List<RepairStep> Received { get; } = [];

    /// <summary>Status returned for each step, in order. Last value repeats.</summary>
    public Queue<RepairStatus> Statuses { get; init; } = new();

    public bool Elevated { get; set; } = true;

    public bool IsElevated() => Elevated;

    public Task<RepairOutcome> ExecuteAsync(RepairStep action, PrinterSnapshot context, CancellationToken ct = default)
    {
        Received.Add(action);
        var status = Statuses.Count > 0 ? Statuses.Dequeue() : RepairStatus.Applied;
        return Task.FromResult(new RepairOutcome(Guid.NewGuid(), context.Id, action.Kind, status, "executed"));
    }
}

/// <summary>Fake store that records saved snapshots.</summary>
public sealed class FakeStore : ISnapshotStore
{
    public List<PrinterSnapshot> Saved { get; } = [];

    public Task SaveAsync(PrinterSnapshot snapshot, CancellationToken ct = default)
    {
        Saved.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task<PrinterSnapshot?> FindLatestForAsync(string printerName, CancellationToken ct = default)
        => Task.FromResult<PrinterSnapshot?>(null);

    public Task<IReadOnlyList<SnapshotSummary>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SnapshotSummary>>([]);

    public Task<PrinterSnapshot?> LoadAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<PrinterSnapshot?>(Saved.FirstOrDefault(s => s.Id == id));

    public Task DeleteAllAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Fake guard: denies steps whose Kind is in the list.</summary>
public sealed class FakeGuard(params RepairActionKind[] deny) : IPolicyGuard
{
    public List<RepairActionKind> Denied { get; } = [.. deny];

    public PolicyDecision Evaluate(RepairStep action, PrinterSnapshot context)
        => Denied.Contains(action.Kind)
            ? new PolicyDecision(false, $"Step {action.Kind} violates RULE #1.")
            : new PolicyDecision(true, "OK");
}

public static class WorkflowFixtures
{
    public static RepairStep Step(RepairActionKind kind, bool destructive = false, bool elevation = false) =>
        new(kind, Description: kind.ToString(), Destructive: destructive, RequiresElevation: elevation);

    public static RepairPlan Plan(params RepairStep[] steps) =>
        new(Guid.NewGuid(), steps, steps.Any(p => p.RequiresElevation));
}

public sealed class RepairWorkflowTests
{
    [Fact]
    public async Task HappyPathRunsAllStepsInOrder()
    {
        var executor = new FakeExecutor();
        var wf = new RepairWorkflow(executor, new FakeGuard(), store: null, capture: new CaptureWorkflow(new FakePrintGateway()));
        var snap = SnapshotFixtures.Snapshot();
        var plan = WorkflowFixtures.Plan(
            WorkflowFixtures.Step(RepairActionKind.ClearQueue),
            WorkflowFixtures.Step(RepairActionKind.RestartSpooler, elevation: true));

        var results = await wf.RunAsync(plan, snap);

        Assert.All(results, r => Assert.Equal(RepairStatus.Applied, r.Status));
        Assert.Equal(2, executor.Received.Count);
        Assert.Equal(RepairActionKind.ClearQueue, executor.Received[0].Kind);
        Assert.Equal(RepairActionKind.RestartSpooler, executor.Received[1].Kind);
    }

    [Fact]
    public async Task StepDeniedByPolicyBecomesSkippedPolicyViolationAndContinues()
    {
        var executor = new FakeExecutor();
        var wf = new RepairWorkflow(executor, new FakeGuard(RepairActionKind.ReinstallFromDriverStore),
            store: null, capture: new CaptureWorkflow(new FakePrintGateway()));
        var snap = SnapshotFixtures.Snapshot();
        var plan = WorkflowFixtures.Plan(
            WorkflowFixtures.Step(RepairActionKind.ReinstallFromDriverStore),
            WorkflowFixtures.Step(RepairActionKind.RestoreDefaults));

        var results = await wf.RunAsync(plan, snap);

        Assert.Equal(RepairStatus.SkippedPolicyViolation, results[0].Status);
        Assert.Contains("RULE #1", results[0].Detail);
        Assert.Equal(RepairStatus.Applied, results[1].Status); // plan continues
    }

    [Fact]
    public async Task DestructiveWithoutSnapshotBecomesSkippedNoSnapshotAndDoesNotRun()
    {
        var executor = new FakeExecutor();
        var wf = new RepairWorkflow(executor, new FakeGuard(), store: null, capture: new CaptureWorkflow(new FakePrintGateway()));
        var plan = WorkflowFixtures.Plan(
            WorkflowFixtures.Step(RepairActionKind.RemoveBrokenInstall, destructive: true),
            WorkflowFixtures.Step(RepairActionKind.ClearQueue));

        var results = await wf.RunAsync(plan, lastGood: null);

        Assert.Equal(RepairStatus.SkippedNoSnapshot, results[0].Status);
        Assert.DoesNotContain(executor.Received, s => s.Kind == RepairActionKind.RemoveBrokenInstall);
        Assert.Equal(RepairStatus.Applied, results[1].Status);
    }

    [Fact]
    public async Task DestructiveWithoutElevationFailsBeforeRunning()
    {
        var executor = new FakeExecutor { Elevated = false };
        var wf = new RepairWorkflow(executor, new FakeGuard(), store: null, capture: new CaptureWorkflow(new FakePrintGateway()));
        var snap = SnapshotFixtures.Snapshot();
        var plan = WorkflowFixtures.Plan(
            WorkflowFixtures.Step(RepairActionKind.RemoveBrokenInstall, destructive: true, elevation: true));

        var results = await wf.RunAsync(plan, snap);

        Assert.Equal(RepairStatus.Failed, results[0].Status);
        Assert.Empty(executor.Received);
    }

    [Fact]
    public async Task FirstDestructiveWritesPreRepairSnapshotExactlyOnce()
    {
        var executor = new FakeExecutor();
        var store = new FakeStore();
        var wf = new RepairWorkflow(executor, new FakeGuard(), store, capture: new CaptureWorkflow(new FakePrintGateway()));
        var snap = SnapshotFixtures.Snapshot();
        var plan = WorkflowFixtures.Plan(
            WorkflowFixtures.Step(RepairActionKind.ClearQueue),
            WorkflowFixtures.Step(RepairActionKind.RemoveBrokenInstall, destructive: true),
            WorkflowFixtures.Step(RepairActionKind.ReinstallFromDriverStore, destructive: true));

        await wf.RunAsync(plan, snap);

        var pre = store.Saved.Where(s => s.Origin == SnapshotOrigin.PreRepair).ToList();
        _ = Assert.Single(pre);
    }

    [Fact]
    public async Task AppliedWritesPostRepairSnapshot()
    {
        var executor = new FakeExecutor();
        var store = new FakeStore();
        var wf = new RepairWorkflow(executor, new FakeGuard(), store, capture: new CaptureWorkflow(new FakePrintGateway()));
        var snap = SnapshotFixtures.Snapshot();
        var plan = WorkflowFixtures.Plan(WorkflowFixtures.Step(RepairActionKind.ClearQueue));

        await wf.RunAsync(plan, snap);

        var post = store.Saved.Where(s => s.Origin == SnapshotOrigin.PostRepair).ToList();
        _ = Assert.Single(post);
    }

    [Fact]
    public async Task NothingAppliedWritesNoPostRepair()
    {
        var executor = new FakeExecutor { Statuses = new Queue<RepairStatus>([RepairStatus.Failed]) };
        var store = new FakeStore();
        var wf = new RepairWorkflow(executor, new FakeGuard(), store, capture: new CaptureWorkflow(new FakePrintGateway()));
        var plan = WorkflowFixtures.Plan(WorkflowFixtures.Step(RepairActionKind.ClearQueue));

        await wf.RunAsync(plan, SnapshotFixtures.Snapshot());

        Assert.DoesNotContain(store.Saved, s => s.Origin == SnapshotOrigin.PostRepair);
    }

    [Fact]
    public async Task WithoutSnapshotAndOnlyDestructivesEndsWithNoRealExecution()
    {
        var executor = new FakeExecutor();
        var wf = new RepairWorkflow(executor, new FakeGuard(), store: null, capture: new CaptureWorkflow(new FakePrintGateway()));
        var plan = WorkflowFixtures.Plan(
            WorkflowFixtures.Step(RepairActionKind.RemoveBrokenInstall, destructive: true),
            WorkflowFixtures.Step(RepairActionKind.ReinstallWithIppClassDriver, destructive: true));

        var results = await wf.RunAsync(plan, lastGood: null);

        Assert.All(results, r => Assert.Equal(RepairStatus.SkippedNoSnapshot, r.Status));
        Assert.Empty(executor.Received);
    }
}

public sealed class CaptureWorkflowTests
{
    [Fact]
    public async Task CaptureBuildsCompleteSnapshotFromGateway()
    {
        var target = TestTargets.Tcp();
        var gateway = new FakePrintGateway(
            printers: [target],
            port: SnapshotFixtures.Port(),
            queue: SnapshotFixtures.Queue(),
            driver: SnapshotFixtures.Driver());
        var wf = new CaptureWorkflow(gateway);

        var snap = await wf.CaptureAsync(target, SnapshotOrigin.Manual);

        Assert.Equal(SnapshotStore.CurrentSchemaVersion, snap.SchemaVersion);
        Assert.Equal(SnapshotOrigin.Manual, snap.Origin);
        Assert.NotNull(snap.Port);
        Assert.NotNull(snap.Queue);
        Assert.NotNull(snap.Driver);
        Assert.False(snap.Id == default);
    }

    [Fact]
    public async Task CaptureWithoutNamedPortOrDriverLeavesFieldsNull()
    {
        var target = TestTargets.Tcp(portName: null) with { DriverName = null };
        var gateway = new FakePrintGateway(queue: SnapshotFixtures.Queue());
        var wf = new CaptureWorkflow(gateway);

        var snap = await wf.CaptureAsync(target, SnapshotOrigin.PreRepair);

        Assert.Null(snap.Port);
        Assert.Null(snap.Driver);
        Assert.NotNull(snap.Queue);
        Assert.Equal(SnapshotOrigin.PreRepair, snap.Origin);
    }
}
