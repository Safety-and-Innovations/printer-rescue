using PrinterRescue.Core;
using PrinterRescue.Core.Interfaces;
using PrinterRescue.Gui;
using Xunit;

namespace PrinterRescue.Core.Tests.Gui;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void SemSelecaoComandosDesabilitados()
    {
        var vm = new MainWindowViewModel(new FakePrintGateway(), new FakeEngine());

        Assert.False(vm.DiagnosticarCommand.CanExecute(null));
        Assert.False(vm.CriarSnapshotCommand.CanExecute(null));
    }

    [Fact]
    public async Task AtualizarCarregaImpressorasEStatus()
    {
        var vm = new MainWindowViewModel(
            new FakePrintGateway(printers: [TestTargets.Tcp()]),
            new FakeEngine());

        await vm.AtualizarAsync();

        _ = Assert.Single(vm.Impressoras);
        Assert.Contains("1 impressora", vm.Status);
    }

    [Fact]
    public async Task SelecionarHabilitaComandosEDiagnosticoPreencheResultados()
    {
        var gateway = new FakePrintGateway(printers: [TestTargets.Tcp()]);
        var vm = new MainWindowViewModel(gateway, new FakeEngine());
        await vm.AtualizarAsync();

        vm.ImpressoraSelecionada = vm.Impressoras[0];

        Assert.True(vm.DiagnosticarCommand.CanExecute(null));
        Assert.True(vm.CriarSnapshotCommand.CanExecute(null));

        await vm.DiagnosticarAsync();

        _ = Assert.Single(vm.Resultados);
        Assert.Equal(CheckId.SpoolerRunning, vm.Resultados[0].Id);
        Assert.Contains("concluído", vm.Status);
    }

    [Fact]
    public async Task DiagnosticoSemSpoolerMostraFalhaNoStatus()
    {
        var gateway = new FakePrintGateway
        {
            ListPrintersError = new InvalidOperationException("RPC indisponível"),
        };
        var vm = new MainWindowViewModel(gateway, new FakeEngine());

        // Atualização com spooler inacessível não estoura — reporta no status.
        await vm.AtualizarAsync();
        Assert.Contains("Erro", vm.Status);
    }

    /// <summary>Engine falso: um check Pass fixo.</summary>
    private sealed class FakeEngine : IDiagnosticEngine
    {
        public Task<DiagnosticReport> DiagnoseAndPlanAsync(PrinterTarget target, CancellationToken ct = default)
            => Task.FromResult(new DiagnosticReport(
                Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow,
                [new CheckOutcome(CheckId.SpoolerRunning, CheckResult.Pass, Severity.Info, "ok")],
                new RepairPlan(Guid.NewGuid(), [], false)));
    }
}
