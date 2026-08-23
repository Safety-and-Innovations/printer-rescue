using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PrinterRescue.Core.Diagnostics;
using PrinterRescue.Core.Diagnostics.Checks;
using PrinterRescue.Core.Interfaces;
using PrinterRescue.Core.Repair;
using PrinterRescue.Adapters.Windows.Winspool;

namespace PrinterRescue.Gui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Composição real quando no Windows; fora dele a janela abre com o
        // gateway indisponível e a barra de status explica o motivo.
        if (OperatingSystem.IsWindows())
        {
            var gateway = new WinspoolPrintGateway();
            IDiagnosticCheck[] checks =
            [
                new SpoolerCheck(),
                new PortOpenCheck(),
                new DriverPresentCheck(),
                new QueueExistsCheck(),
                new QueueNotStuckCheck(),
                new NoDuplicateInstallCheck(),
            ];
            DataContext = new MainWindowViewModel(gateway, new DiagnosticEngine(gateway, checks, new RepairPlanner()));
        }
        else
        {
            DataContext = new MainWindowViewModel(
                new GatewaysIndisponivel(),
                new EnginesIndisponivel());
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

/// <summary>Gateway placeholder para execução fora do Windows: reporta no status.</summary>
internal sealed class GatewaysIndisponivel : Core.Interfaces.IPrintSystemGateway
{
    public Task<System.Collections.Generic.IReadOnlyList<Core.PrinterTarget>> ListPrintersAsync(System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("O acesso às impressoras requer execução no Windows.");

    public Task<Core.PortConfig?> GetPortAsync(string portName, System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Requer Windows.");

    public Task<Core.QueueState> GetQueueStateAsync(string queueName, System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Requer Windows.");

    public Task<Core.DriverInfo?> GetDriverInfoAsync(string driverName, System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Requer Windows.");

    public Task<System.Collections.Generic.IReadOnlyDictionary<string, string>> GetPermissionsSddlAsync(string queueName, System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Requer Windows.");

    public Task<System.Collections.Generic.IReadOnlyDictionary<string, string>> GetDefaultsAsync(string queueName, System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Requer Windows.");
}

/// <summary>Engine placeholder para execução fora do Windows.</summary>
internal sealed class EnginesIndisponivel : Core.Interfaces.IDiagnosticEngine
{
    public Task<Core.DiagnosticReport> DiagnoseAndPlanAsync(Core.PrinterTarget target, System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("O diagnóstico requer execução no Windows.");
}
