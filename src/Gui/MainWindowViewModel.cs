using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PrinterRescue.Core;
using PrinterRescue.Core.Diagnostics;
using PrinterRescue.Core.Interfaces;
using PrinterRescue.Core.Workflows;

namespace PrinterRescue.Gui;

/// <summary>
/// All main-window logic, testable without UI. Receives the Core
/// interfaces via constructor; never blocks the UI thread (pure async).
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IPrintSystemGateway _gateway;
    private readonly IDiagnosticEngine _engine;
    private PrinterItem? _selectedPrinter;
    private string _status = "Ready.";

    public MainWindowViewModel(IPrintSystemGateway gateway, IDiagnosticEngine engine)
    {
        _gateway = gateway;
        _engine = engine;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        DiagnoseCommand = new AsyncRelayCommand(
            _ => DiagnoseAsync(),
            _ => _selectedPrinter is not null);
        CreateSnapshotCommand = new AsyncRelayCommand(
            _ => CreateSnapshotAsync(),
            _ => _selectedPrinter is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PrinterItem> Printers { get; } = [];

    public ObservableCollection<CheckOutcome> Results { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand DiagnoseCommand { get; }

    public ICommand CreateSnapshotCommand { get; }

    public PrinterItem? SelectedPrinter
    {
        get => _selectedPrinter;
        set
        {
            if (ReferenceEquals(value, _selectedPrinter))
            {
                return;
            }

            _selectedPrinter = value;
            Results.Clear();
            OnPropertyChanged();
            RequeryCommands();
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (value == _status)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            var printers = await _gateway.ListPrintersAsync().ConfigureAwait(true);
            Printers.Clear();
            foreach (var p in printers.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                Printers.Add(new PrinterItem(p.Name, p.Protocol.ToString(), p.PortName, p.DriverName));
            }

            Status = $"{Printers.Count} printer(s) found.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = $"Error listing printers: {ex.Message}";
        }
    }

    public async Task DiagnoseAsync()
    {
        var selected = _selectedPrinter;
        if (selected is null)
        {
            return;
        }

        Status = $"Diagnosing '{selected.Name}'…";
        try
        {
            var target = new PrinterTarget(selected.Name, null, selected.Port,
                InferProtocol(selected.Protocol), null, selected.Driver, null);
            var report = await _engine.DiagnoseAndPlanAsync(target).ConfigureAwait(true);

            Results.Clear();
            foreach (var check in report.Checks)
            {
                Results.Add(check);
            }

            Status = report.Plan.Steps.Count > 0
                ? $"Diagnostics complete — plan with {report.Plan.Steps.Count} step(s)."
                : "Diagnostics complete — nothing to do.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = $"Diagnostics error: {ex.Message}";
        }
    }

    public Task CreateSnapshotAsync()
    {
        // Recording uses CaptureWorkflow + ISnapshotStore; in the GUI facade the real
        // store is injected by the bootstrapper. Here it records the intent in the status.
        var selected = _selectedPrinter;
        if (selected is not null)
        {
            Status = $"Snapshot of '{selected.Name}' requested.";
        }

        return Task.CompletedTask;
    }

    private static PrinterProtocol InferProtocol(string text)
        => Enum.TryParse<PrinterProtocol>(text, out var p) ? p : PrinterProtocol.TcpRaw;

    private void RequeryCommands()
    {
        (DiagnoseCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CreateSnapshotCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Printer list row.</summary>
public sealed record PrinterItem(string Name, string Protocol, string? Port, string? Driver)
{
    public string Detail => $"[{Protocol}] port={Port ?? "-"} · driver={Driver ?? "-"}";
}

/// <summary>Safe async command: never async void outside the dispatcher; manual requery.</summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private bool _executing;

    public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
        => !_executing && (_canExecute?.Invoke(parameter) ?? true);

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _executing = true;
        try
        {
            await _execute(parameter).ConfigureAwait(true);
        }
        finally
        {
            _executing = false;
            RaiseCanExecuteChanged();
        }
    }
}
