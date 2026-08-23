using PrinterRescue.Core.Diagnostics.Checks;
using Xunit;

namespace PrinterRescue.Core.Tests.Diagnostics;

public sealed class NoDuplicateInstallCheckTests
{
    [Fact]
    public async Task RunAsyncComInstalacaoUnicaRetornaPass()
    {
        var gateway = new FakePrintGateway(printers: [TestTargets.Tcp()]);
        var check = new NoDuplicateInstallCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckId.NoDuplicateInstall, outcome.Id);
        Assert.Equal(CheckResult.Pass, outcome.Result);
        Assert.Equal(Severity.Info, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncComDuasImpressorasDeMesmoNomeRetornaFail()
    {
        var gateway = new FakePrintGateway(printers:
        [
            TestTargets.Tcp(portName: "IP_192.168.0.40"),
            TestTargets.Usb(),
        ]);
        var check = new NoDuplicateInstallCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckId.NoDuplicateInstall, outcome.Id);
        Assert.Equal(CheckResult.Fail, outcome.Result);
        Assert.Equal(Severity.Error, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncComImpressoraAusenteDaListaRetornaFail()
    {
        var gateway = new FakePrintGateway(printers: []);
        var check = new NoDuplicateInstallCheck();

        var outcome = await check.RunAsync(gateway, TestTargets.Tcp());

        Assert.Equal(CheckId.NoDuplicateInstall, outcome.Id);
        Assert.Equal(CheckResult.Fail, outcome.Result);
        Assert.Equal(Severity.Error, outcome.Severity);
    }

    [Fact]
    public async Task RunAsyncComGatewayNuloLancaArgumentNull()
    {
        var check = new NoDuplicateInstallCheck();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => check.RunAsync(null!, TestTargets.Tcp()));
    }
}
