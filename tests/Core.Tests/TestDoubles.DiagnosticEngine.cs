using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Tests;

/// <summary>Check falso: devolve um outcome pré-definido sem tocar no gateway.</summary>
public sealed class StubCheck : IDiagnosticCheck
{
    private readonly Func<PrinterTarget, CheckOutcome> _executar;

    public StubCheck(CheckId id, CheckOutcome outcome)
        : this(id, _ => outcome)
    {
    }

    public StubCheck(CheckId id, Func<PrinterTarget, CheckOutcome> executar)
    {
        Id = id;
        _executar = executar;
    }

    public CheckId Id { get; }

    public Task<CheckOutcome> RunAsync(IPrintSystemGateway gateway, PrinterTarget target, CancellationToken ct = default)
        => Task.FromResult(_executar(target));
}

/// <summary>Check falso que sempre lança exceção — valida o tratamento do engine.</summary>
public sealed class ThrowingCheck : IDiagnosticCheck
{
    public ThrowingCheck(CheckId id) => Id = id;

    public CheckId Id { get; }

    public Task<CheckOutcome> RunAsync(IPrintSystemGateway gateway, PrinterTarget target, CancellationToken ct = default)
        => throw new InvalidOperationException("boom");
}

/// <summary>Planejador falso que registra os argumentos recebidos do engine.</summary>
public sealed class FakeRepairPlanner : IRepairPlanner
{
    private RepairPlan? _planoRetorno;

    public int Calls { get; private set; }

    public DiagnosticReport? ReceivedReport { get; private set; }

    public PrinterSnapshot? LastGood { get; private set; }

    /// <summary>Quando definido, é o plano devolvido por PlanRepairs.</summary>
    public RepairPlan? PlanoRetorno { get => _planoRetorno; init => _planoRetorno = value; }

    public RepairPlan PlanRepairs(DiagnosticReport report, PrinterSnapshot? lastGood)
    {
        Calls++;
        ReceivedReport = report;
        LastGood = lastGood;
        return _planoRetorno ?? new RepairPlan(report.TargetId, [], false);
    }
}

/// <summary>
/// Loja de snapshots falsa que registra o nome consultado — distinta de FakeSnapshotStore
/// para não acoplar os testes do engine ao comportamento da outra double.
/// </summary>
public sealed class RecordingSnapshotStore : ISnapshotStore
{
    private readonly PrinterSnapshot? _latest;

    public RecordingSnapshotStore(PrinterSnapshot? latest = null) => _latest = latest;

    public string? NomeConsultado { get; private set; }

    public Task SaveAsync(PrinterSnapshot snapshot, CancellationToken ct = default) => Task.CompletedTask;

    public Task<PrinterSnapshot?> FindLatestForAsync(string printerName, CancellationToken ct = default)
    {
        NomeConsultado = printerName;
        return Task.FromResult(_latest);
    }

    public Task<IReadOnlyList<SnapshotSummary>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SnapshotSummary>>([]);

    public Task<PrinterSnapshot?> LoadAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<PrinterSnapshot?>(null);

    public Task DeleteAllAsync(CancellationToken ct = default) => Task.CompletedTask;
}
