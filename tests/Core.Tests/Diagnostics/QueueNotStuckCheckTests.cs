using PrinterRescue.Core.Diagnostics.Checks;
using Xunit;

namespace PrinterRescue.Core.Tests.Diagnostics;

public sealed class QueueNotStuckCheckTests
{
    [Fact]
    public async Task RunAsyncSemTrabalhosPresosRetornaPass()
    {
        var gateway = new FakePrintGateway(queue: new QueueState(
            TestTargets.NomePadrao, Exists: true, StuckJobs: 0,
            DefaultPaperSize: "A4", CopiesDefault: 1, ColorDefault: false, DuplexDefault: true));
        var check = new QueueNotStuckCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckId.QueueNotStuck, outcome.Id);
        Assert.Equal(CheckResult.Pass, outcome.Result);
        Assert.Equal(Severity.Info, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncComTrabalhosPresosRetornaWarn()
    {
        var gateway = new FakePrintGateway(queue: new QueueState(
            TestTargets.NomePadrao, Exists: true, StuckJobs: 3,
            DefaultPaperSize: "A4", CopiesDefault: 1, ColorDefault: false, DuplexDefault: true));
        var check = new QueueNotStuckCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckId.QueueNotStuck, outcome.Id);
        Assert.Equal(CheckResult.Warn, outcome.Result);
        Assert.Equal(Severity.Warning, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncComGatewayNuloLancaArgumentNull()
    {
        var check = new QueueNotStuckCheck();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => check.RunAsync(null!, TestTargets.Tcp()));
    }
}
