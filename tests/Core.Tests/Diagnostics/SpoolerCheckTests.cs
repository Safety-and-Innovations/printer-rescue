using PrinterRescue.Core.Diagnostics.Checks;
using Xunit;

namespace PrinterRescue.Core.Tests.Diagnostics;

public sealed class SpoolerCheckTests
{
    [Fact]
    public async Task RunAsyncWithReachableSpoolerReturnsPass()
    {
        var gateway = new FakePrintGateway();
        var check = new SpoolerCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckId.SpoolerRunning, outcome.Id);
        Assert.Equal(CheckResult.Pass, outcome.Result);
        Assert.Equal(Severity.Info, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncWithUnreachableSpoolerReturnsFail()
    {
        var gateway = new FakePrintGateway { ListPrintersError = new InvalidOperationException("RPC unavailable") };
        var check = new SpoolerCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckResult.Fail, outcome.Result);
        Assert.Equal(Severity.Error, outcome.Severity);
    }
}
