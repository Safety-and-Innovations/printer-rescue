using PrinterRescue.Cli;
using PrinterRescue.Core;
using Xunit;

namespace PrinterRescue.Core.Tests.Cli;

public sealed class ExitCodeMapperTests
{
    private static RepairOutcome Outcome(RepairStatus status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), RepairActionKind.ClearQueue, status, "-");

    // ---- Repair -------------------------------------------------------------

    [Fact]
    public void EmptyPlanReturnsOk()
        => Assert.Equal(ExitCode.Ok, ExitCodeMapper.FromRepair([]));

    [Fact]
    public void AnyAppliedHasPriority()
        => Assert.Equal(ExitCode.RepairApplied, ExitCodeMapper.FromRepair(
            [Outcome(RepairStatus.Applied), Outcome(RepairStatus.Failed)]));

    [Fact]
    public void WithoutAppliedWithPolicyViolation()
        => Assert.Equal(ExitCode.RepairBlockedByPolicy, ExitCodeMapper.FromRepair(
            [Outcome(RepairStatus.SkippedPolicyViolation)]));

    [Fact]
    public void WithoutAppliedWithoutPolicyWithSkippedNoSnapshot()
        => Assert.Equal(ExitCode.SnapshotMissing, ExitCodeMapper.FromRepair(
            [Outcome(RepairStatus.SkippedNoSnapshot)]));

    [Fact]
    public void OnlyFailuresReturnDiagnosticFailed()
        => Assert.Equal(ExitCode.DiagnosticFailed, ExitCodeMapper.FromRepair(
            [Outcome(RepairStatus.Failed), Outcome(RepairStatus.Failed)]));

    // ---- Diagnostics --------------------------------------------------------

    [Fact]
    public void DiagnoseWithoutFailReturnsOk()
        => Assert.Equal(ExitCode.Ok, ExitCodeMapper.FromDiagnostics(
            [new CheckOutcome(CheckId.PortOpen, CheckResult.Pass, Severity.Info, "-"),
             new CheckOutcome(CheckId.QueueNotStuck, CheckResult.Warn, Severity.Warning, "-")]));

    [Fact]
    public void DiagnoseWithFailReturnsDiagnosticFailed()
        => Assert.Equal(ExitCode.DiagnosticFailed, ExitCodeMapper.FromDiagnostics(
            [new CheckOutcome(CheckId.PortOpen, CheckResult.Fail, Severity.Error, "-")]));

    // ---- Formatting ---------------------------------------------------------

    [Fact]
    public void FormatCheckIncludesIdResultAndDetail()
    {
        var line = OutputFormatter.Check(
            new CheckOutcome(CheckId.QueueNotStuck, CheckResult.Warn, Severity.Warning, "3 stuck jobs"));

        Assert.Contains("QueueNotStuck", line);
        Assert.Contains("WARN", line);
        Assert.Contains("3 stuck jobs", line);
    }

    [Fact]
    public void FormatPlanListsNumberedSteps()
    {
        var lines = OutputFormatter.Plan(
        [
            new RepairStep(RepairActionKind.ClearQueue, "Clear queue", Destructive: false, RequiresElevation: false),
            new RepairStep(RepairActionKind.RemoveBrokenInstall, "Remove broken", Destructive: true, RequiresElevation: true),
        ]);

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("1.", lines[0]);
        Assert.Contains("[destructive]", lines[1]);
        Assert.Contains("[elevation]", lines[1]);
    }
}
