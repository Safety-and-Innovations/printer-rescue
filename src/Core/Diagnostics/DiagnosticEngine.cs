using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics;

/// <summary>
/// Executa a sequência determinística de checks na ordem contratual de CheckId,
/// aplica gating do spooler (spooler Fail ⇒ demais checks NotApplicable) e anexa
/// o plano de reparo gerado pelo IRepairPlanner com o último snapshot válido.
/// </summary>
public sealed class DiagnosticEngine : IDiagnosticEngine
{
    /// <summary>Ordem contratual de execução: a própria ordem de declaração do enum.</summary>
    private static readonly CheckId[] OrdemContratual =
    [
        CheckId.SpoolerRunning,
        CheckId.PortOpen,
        CheckId.DriverPresent,
        CheckId.QueueExists,
        CheckId.QueueNotStuck,
        CheckId.NoDuplicateInstall,
    ];

    private readonly IPrintSystemGateway _gateway;
    private readonly IReadOnlyList<IDiagnosticCheck> _checks;
    private readonly IRepairPlanner _planner;
    private readonly ISnapshotStore? _store;

    public DiagnosticEngine(
        IPrintSystemGateway gateway,
        IReadOnlyList<IDiagnosticCheck> checks,
        IRepairPlanner planner,
        ISnapshotStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(planner);
        _gateway = gateway;
        _checks = checks;
        _planner = planner;
        _store = store;
    }

    /// <inheritdoc />
    public async Task<DiagnosticReport> DiagnoseAndPlanAsync(PrinterTarget target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var inicioUtc = DateTime.UtcNow;

        var porId = new Dictionary<CheckId, IDiagnosticCheck>();
        foreach (var check in _checks)
        {
            if (!porId.TryAdd(check.Id, check))
            {
                throw new InvalidOperationException($"Check duplicado injetado: {check.Id}.");
            }
        }

        var resultados = new List<CheckOutcome>(OrdemContratual.Length);
        var spoolerFalhou = false;
        foreach (var id in OrdemContratual)
        {
            ct.ThrowIfCancellationRequested();

            if (spoolerFalhou && id != CheckId.SpoolerRunning)
            {
                resultados.Add(new CheckOutcome(
                    id,
                    CheckResult.NotApplicable,
                    Severity.Info,
                    "Não aplicável: diagnóstico abortado no check do spooler."));
                continue;
            }

            if (!porId.TryGetValue(id, out var check))
            {
                // Check ausente não bloqueia o relatório; fica registrado como não aplicável.
                resultados.Add(new CheckOutcome(id, CheckResult.NotApplicable, Severity.Info, "Check não registrado no engine."));
                continue;
            }

            CheckOutcome outcome;
            try
            {
                outcome = await check.RunAsync(_gateway, target, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Exceção de um check nunca derruba o diagnóstico: vira Fail/Error.
                outcome = new CheckOutcome(id, CheckResult.Fail, Severity.Error,
                    $"Falha inesperada ao executar o check {id}: {ex.Message}");
            }

            resultados.Add(outcome);

            if (id == CheckId.SpoolerRunning && outcome.Result == CheckResult.Fail)
            {
                spoolerFalhou = true;
            }
        }

        var relatorioParcial = new DiagnosticReport(
            AlvoId(target),
            inicioUtc,
            DateTime.UtcNow,
            resultados,
            new RepairPlan(Guid.Empty, [], false));

        PrinterSnapshot? lastGood = null;
        if (_store is not null)
        {
            try
            {
                lastGood = await _store.FindLatestForAsync(target.Name, ct).ConfigureAwait(false);
            }
            catch (System.IO.InvalidDataException)
            {
                // Loja com dados inválidos não derruba o diagnóstico; segue sem snapshot.
            }
        }

        var plano = _planner.PlanRepairs(relatorioParcial, lastGood);

        return relatorioParcial with { Plan = plano };
    }

    /// <summary>
    /// Identificador estável do alvo: derivado do nome da impressora (determinístico
    /// na sessão), pois PrinterTarget não carrega Guid próprio no contrato congelado.
    /// </summary>
    private static Guid AlvoId(PrinterTarget target)
        => new(HashToGuidBytes(target.Name));

    private static byte[] HashToGuidBytes(string texto)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(texto);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        var guid = new byte[16];
        Array.Copy(hash, guid, 16);
        return guid;
    }
}
