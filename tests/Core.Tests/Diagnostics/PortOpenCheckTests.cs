using System.Net;
using System.Net.Sockets;
using PrinterRescue.Core.Diagnostics.Checks;
using Xunit;

namespace PrinterRescue.Core.Tests.Diagnostics;

public sealed class PortOpenCheckTests
{
    [Fact]
    public async Task RunAsyncWithUsbProtocolReturnsNotApplicable()
    {
        var gateway = new FakePrintGateway();
        var check = new PortOpenCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Usb());

        Assert.Equal(CheckId.PortOpen, outcome.Id);
        Assert.Equal(CheckResult.NotApplicable, outcome.Result);
        Assert.Equal(Severity.Info, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncWithWsdProtocolReturnsNotApplicable()
    {
        var gateway = new FakePrintGateway();
        var check = new PortOpenCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Wsd());

        Assert.Equal(CheckResult.NotApplicable, outcome.Result);
    }

    [Fact]
    public async Task RunAsyncWithoutPortNameReturnsFail()
    {
        var gateway = new FakePrintGateway();
        var target = TestTargets.Tcp(portName: null);
        var check = new PortOpenCheck();

        var outcome = await check.RunAsync(gateway, target);

        Assert.Equal(CheckResult.Fail, outcome.Result);
        Assert.Equal(Severity.Error, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncWithPortConfigMissingInGatewayReturnsFail()
    {
        // gateway with no configured port (_port = null) for a TCP target
        var gateway = new FakePrintGateway();
        var check = new PortOpenCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckResult.Fail, outcome.Result);
        Assert.Contains("not found", outcome.Detail);
    }

    [Fact]
    public async Task RunAsyncWithLocalListeningPortReturnsPass()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;

        var gateway = new FakePrintGateway(
            port: new PortConfig("TEST_PORT", "127.0.0.1", endpoint.Port, PrinterProtocol.TcpRaw));
        var check = new PortOpenCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckResult.Pass, outcome.Result);
        Assert.Equal(Severity.Info, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncWithLocalClosedPortReturnsFail()
    {
        // guaranteed-closed free port (acquired then released)
        var port = GetFreePortAndClose();

        var gateway = new FakePrintGateway(
            port: new PortConfig("CLOSED_PORT", "127.0.0.1", port, PrinterProtocol.TcpRaw));
        var check = new PortOpenCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckResult.Fail, outcome.Result);
    }

    private static int GetFreePortAndClose()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
