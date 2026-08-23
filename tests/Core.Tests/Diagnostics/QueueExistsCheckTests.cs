using PrinterRescue.Core.Diagnostics.Checks;
using Xunit;

namespace PrinterRescue.Core.Tests.Diagnostics;

public sealed class QueueExistsCheckTests
{
    [Fact]
    public async Task RunAsyncComFilaExistenteRetornaPass()
    {
        var gateway = new FakePrintGateway(queue: new QueueState(
            TestTargets.NomePadrao, Exists: true, StuckJobs: 0,
            DefaultPaperSize: "A4", CopiesDefault: 1, ColorDefault: false, DuplexDefault: true));
        var check = new QueueExistsCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckId.QueueExists, outcome.Id);
        Assert.Equal(CheckResult.Pass, outcome.Result);
        Assert.Equal(Severity.Info, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncComFilaInexistenteRetornaFail()
    {
        var gateway = new FakePrintGateway(queue: new QueueState(
            TestTargets.NomePadrao, Exists: false, StuckJobs: 0,
            DefaultPaperSize: null, CopiesDefault: 1, ColorDefault: false, DuplexDefault: true));
        var check = new QueueExistsCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckId.QueueExists, outcome.Id);
        Assert.Equal(CheckResult.Fail, outcome.Result);
        Assert.Equal(Severity.Error, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncComGatewayNuloLancaArgumentNull()
    {
        var check = new QueueExistsCheck();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => check.RunAsync(null!, TestTargets.Tcp()));
    }
}
