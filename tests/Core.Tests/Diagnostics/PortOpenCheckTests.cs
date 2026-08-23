using System.Net;
using System.Net.Sockets;
using PrinterRescue.Core.Diagnostics.Checks;
using Xunit;

namespace PrinterRescue.Core.Tests.Diagnostics;

public sealed class PortOpenCheckTests
{
    [Fact]
    public async Task RunAsyncComProtocoloUsbRetornaNotApplicable()
    {
        var gateway = new FakePrintGateway();
        var check = new PortOpenCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Usb());

        Assert.Equal(CheckId.PortOpen, outcome.Id);
        Assert.Equal(CheckResult.NotApplicable, outcome.Result);
        Assert.Equal(Severity.Info, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncComProtocoloWsdRetornaNotApplicable()
    {
        var gateway = new FakePrintGateway();
        var check = new PortOpenCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Wsd());

        Assert.Equal(CheckResult.NotApplicable, outcome.Result);
    }

    [Fact]
    public async Task RunAsyncSemNomeDePortaRetornaFail()
    {
        var gateway = new FakePrintGateway();
        var alvo = TestTargets.Tcp(portName: null);
        var check = new PortOpenCheck();

        var outcome = await check.RunAsync(gateway, alvo);

        Assert.Equal(CheckResult.Fail, outcome.Result);
        Assert.Equal(Severity.Error, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncComConfiguracaoDePortaAusenteNoGatewayRetornaFail()
    {
        // gateway sem porta configurada (_port = null) para um alvo TCP
        var gateway = new FakePrintGateway();
        var check = new PortOpenCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckResult.Fail, outcome.Result);
        Assert.Contains("não encontrada", outcome.Detail);
    }

    [Fact]
    public async Task RunAsyncComPortaOuvinteLocalRetornaPass()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;

        var gateway = new FakePrintGateway(
            port: new PortConfig("PORTA_TESTE", "127.0.0.1", endpoint.Port, PrinterProtocol.TcpRaw));
        var check = new PortOpenCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckResult.Pass, outcome.Result);
        Assert.Equal(Severity.Info, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncComPortaFechadaLocalRetornaFail()
    {
        // porta livre garantidamente fechada (obtida e liberada)
        var porta = ObterPortaLivreEFecha();

        var gateway = new FakePrintGateway(
            port: new PortConfig("PORTA_FECHADA", "127.0.0.1", porta, PrinterProtocol.TcpRaw));
        var check = new PortOpenCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckResult.Fail, outcome.Result);
    }

    private static int ObterPortaLivreEFecha()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var porta = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return porta;
    }
}
