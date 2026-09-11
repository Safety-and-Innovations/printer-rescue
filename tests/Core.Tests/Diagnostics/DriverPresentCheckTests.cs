using PrinterRescue.Core.Diagnostics.Checks;
using Xunit;

namespace PrinterRescue.Core.Tests.Diagnostics;

public sealed class DriverPresentCheckTests
{
    [Fact]
    public async Task RunAsyncWithDriverPresentInDriverStoreReturnsPass()
    {
        var gateway = new FakePrintGateway(driver: new DriverInfo(
            Name: "HP Universal PCL6", Version: "3.12.0.0", InfName: "hpcu270u.inf",
            PresentInDriverStore: true, IsIppClassDriver: false));
        var check = new DriverPresentCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckId.DriverPresent, outcome.Id);
        Assert.Equal(CheckResult.Pass, outcome.Result);
        Assert.Equal(Severity.Info, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncWithDriverNotFoundReturnsFail()
    {
        var gateway = new FakePrintGateway(driver: null);
        var check = new DriverPresentCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckId.DriverPresent, outcome.Id);
        Assert.Equal(CheckResult.Fail, outcome.Result);
        Assert.Equal(Severity.Error, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncWithDriverMissingFromDriverStoreReturnsFail()
    {
        var gateway = new FakePrintGateway(driver: new DriverInfo(
            Name: "HP Universal PCL6", Version: "3.12.0.0", InfName: null,
            PresentInDriverStore: false, IsIppClassDriver: false));
        var check = new DriverPresentCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckId.DriverPresent, outcome.Id);
        Assert.Equal(CheckResult.Fail, outcome.Result);
        Assert.Equal(Severity.Error, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncWithNullDriverNameReturnsNotApplicable()
    {
        var gateway = new FakePrintGateway();
        var check = new DriverPresentCheck();
        var target = new PrinterTarget(
            Name: TestTargets.DefaultName, ShareName: null, PortName: "IP_192.168.0.40",
            Protocol: PrinterProtocol.TcpRaw, DeviceId: null, DriverName: null, DriverVersion: null);

        var outcome = await check.RunAsync(gateway, target);

        Assert.Equal(CheckId.DriverPresent, outcome.Id);
        Assert.Equal(CheckResult.NotApplicable, outcome.Result);
        Assert.Equal(Severity.Info, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncWithNullGatewayThrowsArgumentNull()
    {
        var check = new DriverPresentCheck();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => check.RunAsync(null!, TestTargets.Tcp()));
    }
}
