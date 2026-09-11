using PrinterRescue.Core.Diagnostics;
using PrinterRescue.Core.Interfaces;
using Xunit;

namespace PrinterRescue.Core.Tests.Diagnostics;

public sealed class DiagnosticEngineTests
{
    private static readonly PrinterTarget Target = TestTargets.Tcp();

    [Fact]
    public async Task RunsChecksInEnumOrderEvenWithUnorderedList()
    {
        var checks = new IDiagnosticCheck[]
        {
            new StubCheck(CheckId.NoDuplicateInstall, Pass(CheckId.NoDuplicateInstall)),
            new StubCheck(CheckId.QueueNotStuck, Pass(CheckId.QueueNotStuck)),
            new StubCheck(CheckId.SpoolerRunning, Pass(CheckId.SpoolerRunning)),
            new StubCheck(CheckId.DriverPresent, Pass(CheckId.DriverPresent)),
            new StubCheck(CheckId.PortOpen, Pass(CheckId.PortOpen)),
            new StubCheck(CheckId.QueueExists, Pass(CheckId.QueueExists)),
        };

        var report = await NewEngine(checks).DiagnoseAndPlanAsync(Target);

        Assert.Equal(
            new[]
            {
                CheckId.SpoolerRunning,
                CheckId.PortOpen,
                CheckId.DriverPresent,
                CheckId.QueueExists,
                CheckId.QueueNotStuck,
                CheckId.NoDuplicateInstall,
            },
            report.Checks.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task WithFailedSpoolerOtherChecksBecomeNotApplicableWithGating()
    {
        var checks = new IDiagnosticCheck[]
        {
            new StubCheck(CheckId.SpoolerRunning, Fail(CheckId.SpoolerRunning)),
            new StubCheck(CheckId.PortOpen, Pass(CheckId.PortOpen)),
            new StubCheck(CheckId.DriverPresent, Pass(CheckId.DriverPresent)),
            new StubCheck(CheckId.QueueExists, Pass(CheckId.QueueExists)),
            new StubCheck(CheckId.QueueNotStuck, Pass(CheckId.QueueNotStuck)),
            new StubCheck(CheckId.NoDuplicateInstall, Pass(CheckId.NoDuplicateInstall)),
        };

        var report = await NewEngine(checks).DiagnoseAndPlanAsync(Target);

        Assert.Equal(CheckResult.Fail, report.Checks[0].Result);
        foreach (var rest in report.Checks.Skip(1))
        {
            Assert.Equal(CheckResult.NotApplicable, rest.Result);
            Assert.Equal(Severity.Info, rest.Severity);
            Assert.Contains("spooler", rest.Detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CheckExceptionBecomesFailWithoutThrowing()
    {
        var checks = new IDiagnosticCheck[]
        {
            new StubCheck(CheckId.SpoolerRunning, Pass(CheckId.SpoolerRunning)),
            new ThrowingCheck(CheckId.DriverPresent),
            new StubCheck(CheckId.QueueExists, Pass(CheckId.QueueExists)),
        };

        var report = await NewEngine(checks).DiagnoseAndPlanAsync(Target);

        var driver = Assert.Single(report.Checks, c => c.Id == CheckId.DriverPresent);
        Assert.Equal(CheckResult.Fail, driver.Result);
        Assert.Equal(Severity.Error, driver.Severity);
        Assert.Contains("boom", driver.Detail, StringComparison.OrdinalIgnoreCase);
        // The exception does not interrupt diagnostics: the next check still runs.
        Assert.Single(report.Checks, c => c.Id == CheckId.QueueExists);
    }

    [Fact]
    public async Task UsesSnapshotStoreToResolveLastGood()
    {
        var snapshot = CreateSnapshot();
        var store = new RecordingSnapshotStore(snapshot);
        var planner = new FakeRepairPlanner();

        _ = await NewEngine(AllPassing(), planner, store).DiagnoseAndPlanAsync(Target);

        Assert.Equal(TestTargets.DefaultName, store.QueriedName);
        Assert.Equal(snapshot, planner.LastGood);
    }

    [Fact]
    public async Task WithoutSnapshotStorePassesNullLastGoodToPlanner()
    {
        var planner = new FakeRepairPlanner();

        _ = await NewEngine(AllPassing(), planner, store: null).DiagnoseAndPlanAsync(Target);

        Assert.Null(planner.LastGood);
        Assert.Equal(1, planner.Calls);
    }

    [Fact]
    public async Task ReportHasRealTimestampsAndAttachedPlan()
    {
        var before = DateTime.UtcNow;

        var report = await NewEngine(AllPassing()).DiagnoseAndPlanAsync(Target);

        var after = DateTime.UtcNow;
        Assert.NotEqual(Guid.Empty, report.TargetId);
        Assert.InRange(report.StartedAtUtc, before, after);
        Assert.InRange(report.FinishedAtUtc, report.StartedAtUtc, after);
        Assert.Equal(6, report.Checks.Count);
    }

    [Fact]
    public async Task ConstructorWithNullArgumentThrows()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Task.FromResult(new DiagnosticEngine(null!, [], new FakeRepairPlanner())));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Task.FromResult(new DiagnosticEngine(new FakePrintGateway(), null!, new FakeRepairPlanner())));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Task.FromResult(new DiagnosticEngine(new FakePrintGateway(), [], null!)));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new DiagnosticEngine(new FakePrintGateway(), AllPassing(), new FakeRepairPlanner())
                .DiagnoseAndPlanAsync(null!));
    }

    private static DiagnosticEngine NewEngine(
        IReadOnlyList<IDiagnosticCheck> checks,
        IRepairPlanner? planner = null,
        ISnapshotStore? store = null)
        => new(new FakePrintGateway(), checks, planner ?? new FakeRepairPlanner(), store);

    private static IReadOnlyList<IDiagnosticCheck> AllPassing() =>
    [
        new StubCheck(CheckId.SpoolerRunning, Pass(CheckId.SpoolerRunning)),
        new StubCheck(CheckId.PortOpen, Pass(CheckId.PortOpen)),
        new StubCheck(CheckId.DriverPresent, Pass(CheckId.DriverPresent)),
        new StubCheck(CheckId.QueueExists, Pass(CheckId.QueueExists)),
        new StubCheck(CheckId.QueueNotStuck, Pass(CheckId.QueueNotStuck)),
        new StubCheck(CheckId.NoDuplicateInstall, Pass(CheckId.NoDuplicateInstall)),
    ];

    private static CheckOutcome Pass(CheckId id) => new(id, CheckResult.Pass, Severity.Info, "OK (stub).");

    private static CheckOutcome Fail(CheckId id) => new(id, CheckResult.Fail, Severity.Error, "Failed (stub).");

    private static PrinterSnapshot CreateSnapshot() => new(
        Guid.NewGuid(),
        DateTime.UtcNow.AddDays(-1),
        SnapshotOrigin.Manual,
        Target,
        Port: null,
        Queue: null,
        Driver: null,
        Permissions: new Dictionary<string, string>(),
        Defaults: new Dictionary<string, string>(),
        SchemaVersion: "1");
}
