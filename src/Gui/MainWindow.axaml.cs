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

        // Real composition on Windows; elsewhere the window opens with the
        // gateway unavailable and the status bar explains why.
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
                new UnavailableGateway(),
                new UnavailableEngine());
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

/// <summary>Placeholder gateway for running off Windows: reports via status.</summary>
internal sealed class UnavailableGateway : Core.Interfaces.IPrintSystemGateway
{
    public Task<System.Collections.Generic.IReadOnlyList<Core.PrinterTarget>> ListPrintersAsync(System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Printer access requires running on Windows.");

    public Task<Core.PortConfig?> GetPortAsync(string portName, System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Requires Windows.");

    public Task<Core.QueueState> GetQueueStateAsync(string queueName, System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Requires Windows.");

    public Task<Core.DriverInfo?> GetDriverInfoAsync(string driverName, System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Requires Windows.");

    public Task<System.Collections.Generic.IReadOnlyDictionary<string, string>> GetPermissionsSddlAsync(string queueName, System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Requires Windows.");

    public Task<System.Collections.Generic.IReadOnlyDictionary<string, string>> GetDefaultsAsync(string queueName, System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Requires Windows.");
}

/// <summary>Placeholder engine for running off Windows.</summary>
internal sealed class UnavailableEngine : Core.Interfaces.IDiagnosticEngine
{
    public Task<Core.DiagnosticReport> DiagnoseAndPlanAsync(Core.PrinterTarget target, System.Threading.CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Diagnostics requires running on Windows.");
}
