using PrinterRescue.Core.Interfaces;
using PrinterRescue.Core.Repair;
using Xunit;

namespace PrinterRescue.Core.Tests.Repair;

/// <summary>Repair plan generator tests: contractual order, snapshot, and conservatism.</summary>
public sealed class RepairPlannerTests
{
    private readonly RepairPlanner _planner = new();

    [Fact]
    public void QueueWarnGeneratesClearQueueBeforeRestartSpooler()
    {
        var report = CreateReport(queue: CheckResult.Warn);

        var plan = _planner.PlanRepairs(report, lastGood: null);

        Assert.Contains(plan.Steps, static s => s.Kind == RepairActionKind.ClearQueue);
        Assert.Contains(plan.Steps, static s => s.Kind == RepairActionKind.RestartSpooler);
        Assert.True(Index(plan, RepairActionKind.ClearQueue) < Index(plan, RepairActionKind.RestartSpooler));
    }

    [Fact]
    public void PortFailWithSnapshotGeneratesConsistentRestorePort()
    {
        var goodPort = new PortConfig("IP_192.168.0.40", "192.168.0.40", 9100, PrinterProtocol.TcpRaw);
        var snapshot = CreateSnapshot(port: goodPort);
        var report = CreateReport(port: CheckResult.Fail);

        var plan = _planner.PlanRepairs(report, snapshot);

        var portStep = Assert.Single(plan.Steps, static s => s.Kind == RepairActionKind.RestorePort);
        Assert.False(portStep.Destructive);
        Assert.True(portStep.RequiresElevation);
        // Consistency with the snapshot: the good port name and address appear in the description.
        Assert.Contains(goodPort.PortName, portStep.Description, StringComparison.Ordinal);
        Assert.Contains(goodPort.HostAddress, portStep.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverFailWithoutIppClassDriverPlansReinstallWithIppBeforeDriverStore()
    {
        var badDriver = new DriverInfo("HP Universal PCL6", "3.12.0.0", InfName: null, PresentInDriverStore: true, IsIppClassDriver: false);
        var snapshot = CreateSnapshot(driver: badDriver);
        var report = CreateReport(driver: CheckResult.Fail);

        var plan = _planner.PlanRepairs(report, snapshot);

        Assert.Contains(plan.Steps, static s => s.Kind == RepairActionKind.ReinstallWithIppClassDriver);
        Assert.Contains(plan.Steps, static s => s.Kind == RepairActionKind.ReinstallFromDriverStore);
        Assert.True(
            Index(plan, RepairActionKind.ReinstallWithIppClassDriver) < Index(plan, RepairActionKind.ReinstallFromDriverStore));
    }

    [Fact]
    public void WithoutSnapshotNoDestructiveStepEntersPlan()
    {
        var report = CreateReport(port: CheckResult.Fail, driver: CheckResult.Fail);

        var plan = _planner.PlanRepairs(report, lastGood: null);

        Assert.DoesNotContain(plan.Steps, static s => s.Destructive);
        Assert.DoesNotContain(plan.Steps, static s => s.Kind == RepairActionKind.RemoveBrokenInstall);
        Assert.DoesNotContain(plan.Steps, static s => s.Kind == RepairActionKind.ReinstallWithIppClassDriver);
        Assert.DoesNotContain(plan.Steps, static s => s.Kind == RepairActionKind.ReinstallFromDriverStore);
        // Potential destructive steps are recorded as SkippedNoSnapshot.
        Assert.Contains("SkippedNoSnapshot", plan.Steps[^1].Description, StringComparison.Ordinal);
    }

    [Fact]
    public void AllPassProducesEmptyPlan()
    {
        var report = CreateReport();

        var plan = _planner.PlanRepairs(report, lastGood: null);

        Assert.Empty(plan.Steps);
        Assert.False(plan.RequiresElevation);
    }

    [Fact]
    public void GlobalOrderIsContractual()
    {
        var report = CreateReport(queue: CheckResult.Warn, port: CheckResult.Fail, driver: CheckResult.Fail);
        var snapshot = CreateSnapshot(port: new PortConfig("IP_192.168.0.40", "192.168.0.40", 9100, PrinterProtocol.TcpRaw));

        var plan = _planner.PlanRepairs(report, snapshot);

        var kinds = plan.Steps.Select(static s => s.Kind).ToList();
        Assert.True(kinds.Count >= 5, $"Plan should have at least 5 steps, got {kinds.Count}.");
        foreach (var (previous, next) in new[]
                 {
                     (RepairActionKind.ClearQueue, RepairActionKind.RestartSpooler),
                     (RepairActionKind.RestartSpooler, RepairActionKind.RestorePort),
                     (RepairActionKind.RestorePort, RepairActionKind.ReinstallWithIppClassDriver),
                     (RepairActionKind.ReinstallWithIppClassDriver, RepairActionKind.ReinstallFromDriverStore),
                     (RepairActionKind.ReinstallFromDriverStore, RepairActionKind.RemoveBrokenInstall),
                 })
        {
            Assert.True(kinds.IndexOf(previous) < kinds.IndexOf(next), $"{previous} must come before {next}.");
        }
    }

    [Fact]
    public void DestructiveOnlyOnReinstallAndRemovalSteps()
    {
        var report = CreateReport(queue: CheckResult.Warn, driver: CheckResult.Fail);
        var snapshot = CreateSnapshot();

        var plan = _planner.PlanRepairs(report, snapshot);

        foreach (var step in plan.Steps)
        {
            if (step.Kind is RepairActionKind.RemoveBrokenInstall
                or RepairActionKind.ReinstallWithIppClassDriver
                or RepairActionKind.ReinstallFromDriverStore)
            {
                Assert.True(step.Destructive, $"{step.Kind} must be destructive.");
            }
            else
            {
                Assert.False(step.Destructive, $"{step.Kind} must not be destructive.");
            }
        }
    }

    [Fact]
    public void ElevationRequiredOnContractualSteps()
    {
        var report = CreateReport(queue: CheckResult.Warn, port: CheckResult.Fail, driver: CheckResult.Fail);
        var snapshot = CreateSnapshot(port: new PortConfig("IP_192.168.0.40", "192.168.0.40", 9100, PrinterProtocol.TcpRaw));

        var plan = _planner.PlanRepairs(report, snapshot);

        foreach (var step in plan.Steps)
        {
            if (step.Kind is RepairActionKind.RestartSpooler
                or RepairActionKind.RestorePort
                or RepairActionKind.RemoveBrokenInstall
                or RepairActionKind.ReinstallWithIppClassDriver
                or RepairActionKind.ReinstallFromDriverStore)
            {
                Assert.True(step.RequiresElevation, $"{step.Kind} requires elevation.");
            }
        }

        Assert.True(plan.RequiresElevation);
    }

    private static int Index(RepairPlan plan, RepairActionKind kind)
    {
        var index = plan.Steps.ToList().FindIndex(s => s.Kind == kind);
        Assert.True(index >= 0, $"Plan does not contain {kind}.");
        return index;
    }

    private static DiagnosticReport CreateReport(
        CheckResult queue = CheckResult.Pass,
        CheckResult port = CheckResult.Pass,
        CheckResult driver = CheckResult.Pass)
    {
        var now = DateTime.UtcNow;
        var checks = new List<CheckOutcome>
        {
            new(CheckId.SpoolerRunning, CheckResult.Pass, Severity.Info, "Spooler reachable."),
            new(CheckId.PortOpen, port, port == CheckResult.Pass ? Severity.Info : Severity.Error, $"Port: {port}."),
            new(CheckId.DriverPresent, driver, driver == CheckResult.Pass ? Severity.Info : Severity.Error, $"Driver: {driver}."),
            new(CheckId.QueueExists, CheckResult.Pass, Severity.Info, "Queue exists."),
            new(CheckId.QueueNotStuck, queue, queue == CheckResult.Pass ? Severity.Info : queue == CheckResult.Warn ? Severity.Warning : Severity.Error, $"Queue: {queue}."),
            new(CheckId.NoDuplicateInstall, CheckResult.Pass, Severity.Info, "No duplicates."),
        };

        return new DiagnosticReport(Guid.NewGuid(), now, now.AddSeconds(2), checks, new RepairPlan(Guid.NewGuid(), [], false));
    }

    private static PrinterSnapshot CreateSnapshot(
        PortConfig? port = null,
        DriverInfo? driver = null)
    {
        var target = TestTargets.Tcp();
        return new PrinterSnapshot(
            Id: Guid.NewGuid(),
            CreatedAtUtc: DateTime.UtcNow,
            Origin: SnapshotOrigin.PreRepair,
            Target: target,
            Port: port,
            Queue: new QueueState(target.Name, Exists: true, StuckJobs: 0, DefaultPaperSize: "A4", CopiesDefault: 1, ColorDefault: false, DuplexDefault: true),
            Driver: driver,
            Permissions: new Dictionary<string, string>(),
            Defaults: new Dictionary<string, string>(),
            SchemaVersion: "1.0");
    }
}
