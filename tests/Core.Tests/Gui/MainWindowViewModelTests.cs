using PrinterRescue.Core;
using PrinterRescue.Core.Interfaces;
using PrinterRescue.Gui;
using Xunit;

namespace PrinterRescue.Core.Tests.Gui;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void WithoutSelectionCommandsDisabled()
    {
        var vm = new MainWindowViewModel(new FakePrintGateway(), new FakeEngine());

        Assert.False(vm.DiagnoseCommand.CanExecute(null));
        Assert.False(vm.CreateSnapshotCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshLoadsPrintersAndStatus()
    {
        var vm = new MainWindowViewModel(
            new FakePrintGateway(printers: [TestTargets.Tcp()]),
            new FakeEngine());

        await vm.RefreshAsync();

        _ = Assert.Single(vm.Printers);
        Assert.Contains("1 printer", vm.Status);
    }

    [Fact]
    public async Task SelectingEnablesCommandsAndDiagnoseFillsResults()
    {
        var gateway = new FakePrintGateway(printers: [TestTargets.Tcp()]);
        var vm = new MainWindowViewModel(gateway, new FakeEngine());
        await vm.RefreshAsync();

        vm.SelectedPrinter = vm.Printers[0];

        Assert.True(vm.DiagnoseCommand.CanExecute(null));
        Assert.True(vm.CreateSnapshotCommand.CanExecute(null));

        await vm.DiagnoseAsync();

        _ = Assert.Single(vm.Results);
        Assert.Equal(CheckId.SpoolerRunning, vm.Results[0].Id);
        Assert.Contains("complete", vm.Status);
    }

    [Fact]
    public async Task DiagnoseWithoutSpoolerShowsFailureInStatus()
    {
        var gateway = new FakePrintGateway
        {
            ListPrintersError = new InvalidOperationException("RPC unavailable"),
        };
        var vm = new MainWindowViewModel(gateway, new FakeEngine());

        // Refresh with an unreachable spooler does not throw — reports via status.
        await vm.RefreshAsync();
        Assert.Contains("Error", vm.Status);
    }

    /// <summary>Fake engine: one fixed Pass check.</summary>
    private sealed class FakeEngine : IDiagnosticEngine
    {
        public Task<DiagnosticReport> DiagnoseAndPlanAsync(PrinterTarget target, CancellationToken ct = default)
            => Task.FromResult(new DiagnosticReport(
                Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow,
                [new CheckOutcome(CheckId.SpoolerRunning, CheckResult.Pass, Severity.Info, "ok")],
                new RepairPlan(Guid.NewGuid(), [], false)));
    }
}
