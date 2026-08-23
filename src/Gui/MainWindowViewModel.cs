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
/// Toda a lógica da janela principal, testável sem UI. Recebe as interfaces
/// do Core por construtor; nunca bloqueia a thread da UI (async puro).
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IPrintSystemGateway _gateway;
    private readonly IDiagnosticEngine _engine;
    private ImpressoraItem? _impressoraSelecionada;
    private string _status = "Pronto.";

    public MainWindowViewModel(IPrintSystemGateway gateway, IDiagnosticEngine engine)
    {
        _gateway = gateway;
        _engine = engine;
        AtualizarCommand = new AsyncRelayCommand(_ => AtualizarAsync());
        DiagnosticarCommand = new AsyncRelayCommand(
            _ => DiagnosticarAsync(),
            _ => _impressoraSelecionada is not null);
        CriarSnapshotCommand = new AsyncRelayCommand(
            _ => CriarSnapshotAsync(),
            _ => _impressoraSelecionada is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ImpressoraItem> Impressoras { get; } = [];

    public ObservableCollection<CheckOutcome> Resultados { get; } = [];

    public ICommand AtualizarCommand { get; }

    public ICommand DiagnosticarCommand { get; }

    public ICommand CriarSnapshotCommand { get; }

    public ImpressoraItem? ImpressoraSelecionada
    {
        get => _impressoraSelecionada;
        set
        {
            if (ReferenceEquals(value, _impressoraSelecionada))
            {
                return;
            }

            _impressoraSelecionada = value;
            Resultados.Clear();
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

    public async Task AtualizarAsync()
    {
        try
        {
            var impressoras = await _gateway.ListPrintersAsync().ConfigureAwait(true);
            Impressoras.Clear();
            foreach (var p in impressoras.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                Impressoras.Add(new ImpressoraItem(p.Name, p.Protocol.ToString(), p.PortName, p.DriverName));
            }

            Status = $"{Impressoras.Count} impressora(s) encontrada(s).";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = $"Erro ao listar impressoras: {ex.Message}";
        }
    }

    public async Task DiagnosticarAsync()
    {
        var selecionada = _impressoraSelecionada;
        if (selecionada is null)
        {
            return;
        }

        Status = $"Diagnosticando '{selecionada.Nome}'…";
        try
        {
            var alvo = new PrinterTarget(selecionada.Nome, null, selecionada.Porta,
                InferirProtocolo(selecionada.Protocolo), null, selecionada.Driver, null);
            var report = await _engine.DiagnoseAndPlanAsync(alvo).ConfigureAwait(true);

            Resultados.Clear();
            foreach (var check in report.Checks)
            {
                Resultados.Add(check);
            }

            Status = report.Plan.Steps.Count > 0
                ? $"Diagnóstico concluído — plano com {report.Plan.Steps.Count} passo(s)."
                : "Diagnóstico concluído — nada a fazer.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = $"Erro no diagnóstico: {ex.Message}";
        }
    }

    public Task CriarSnapshotAsync()
    {
        // A gravação usa CaptureWorkflow + ISnapshotStore; na fachada GUI o store
        // real é injetado pelo bootstrapper. Aqui registra a intenção no status.
        var selecionada = _impressoraSelecionada;
        if (selecionada is not null)
        {
            Status = $"Snapshot de '{selecionada.Nome}' solicitado.";
        }

        return Task.CompletedTask;
    }

    private static PrinterProtocol InferirProtocolo(string texto)
        => Enum.TryParse<PrinterProtocol>(texto, out var p) ? p : PrinterProtocol.TcpRaw;

    private void RequeryCommands()
    {
        (DiagnosticarCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CriarSnapshotCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? nome = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}

/// <summary>Linha da lista de impressoras.</summary>
public sealed record ImpressoraItem(string Nome, string Protocolo, string? Porta, string? Driver)
{
    public string Detalhe => $"[{Protocolo}] porta={Porta ?? "-"} · driver={Driver ?? "-"}";
}

/// <summary>AsyncCommand seguro: nunca async void fora do dispatcher; requery manual.</summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _executar;
    private readonly Predicate<object?>? _podeExecutar;
    private bool _executando;

    public AsyncRelayCommand(Func<object?, Task> executar, Predicate<object?>? podeExecutar = null)
    {
        _executar = executar;
        _podeExecutar = podeExecutar;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
        => !_executando && (_podeExecutar?.Invoke(parameter) ?? true);

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _executando = true;
        try
        {
            await _executar(parameter).ConfigureAwait(true);
        }
        finally
        {
            _executando = false;
            RaiseCanExecuteChanged();
        }
    }
}
