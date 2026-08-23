using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Workflows;

/// <summary>
/// Executa um plano de reparo passo a passo com as garantias do produto:
/// guarda de política antes de tudo (REGRA Nº 1), elevação obrigatória para
/// passos que a exigem, snapshot pré-reparo antes do primeiro passo destrutivo
/// e snapshot pós-reparo único se algo foi aplicado.
/// </summary>
public sealed class RepairWorkflow
{
    private readonly IRepairExecutor _executor;
    private readonly IPolicyGuard _guard;
    private readonly ISnapshotStore? _store;
    private readonly CaptureWorkflow? _capture;

    public RepairWorkflow(IRepairExecutor executor, IPolicyGuard guard, ISnapshotStore? store, CaptureWorkflow? capture)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(guard);
        _executor = executor;
        _guard = guard;
        _store = store;
        _capture = capture;
    }

    public async Task<IReadOnlyList<RepairOutcome>> RunAsync(RepairPlan plan, PrinterSnapshot? lastGood, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var resultados = new List<RepairOutcome>();
        var capturaPreFeita = false;
        var aplicouAlgo = false;

        foreach (var passo in plan.Steps)
        {
            ct.ThrowIfCancellationRequested();

            // 1. Política primeiro: nada executa sem passar pela REGRA Nº 1.
            var decisao = _guard.Evaluate(passo, lastGood ?? SemContexto());
            if (!decisao.Allowed)
            {
                resultados.Add(SemExecucao(plan, passo, RepairStatus.SkippedPolicyViolation, decisao.Reason));
                continue;
            }

            // 2. Elevação: passo que exige admin sem tê-la falha antes de executar.
            if (passo.RequiresElevation && !_executor.IsElevated())
            {
                resultados.Add(SemExecucao(plan, passo, RepairStatus.Failed,
                    $"Passo '{passo.Kind}' exige privilégio administrativo e o processo não está elevado."));
                continue;
            }

            // 3. Destrutivo exige snapshot válido para reversão.
            if (passo.Destructive && lastGood is null)
            {
                resultados.Add(SemExecucao(plan, passo, RepairStatus.SkippedNoSnapshot,
                    $"[SkippedNoSnapshot] Passo destrutivo '{passo.Kind}' exige snapshot válido anterior."));
                continue;
            }

            // 4. Antes do primeiro passo destrutivo: grava snapshot pré-reparo uma única vez.
            if (passo.Destructive && !capturaPreFeita && lastGood is not null)
            {
                await CapturarAsync(lastGood.Target, SnapshotOrigin.PreRepair, ct).ConfigureAwait(false);
                capturaPreFeita = true;
            }

            var contexto = lastGood ?? SemContexto();
            var resultado = await _executor.ExecuteAsync(passo, contexto, ct).ConfigureAwait(false);
            aplicouAlgo |= resultado.Status == RepairStatus.Applied;
            resultados.Add(resultado);
        }

        // 5. Pós-reparo: registra o novo estado se algo foi aplicado.
        if (aplicouAlgo && lastGood is not null)
        {
            await CapturarAsync(lastGood.Target, SnapshotOrigin.PostRepair, ct).ConfigureAwait(false);
        }

        return resultados;
    }

    private async Task CapturarAsync(PrinterTarget alvo, SnapshotOrigin origem, CancellationToken ct)
    {
        if (_store is null || _capture is null)
        {
            return;
        }

        var snapshot = await _capture.CaptureAsync(alvo, origem, ct).ConfigureAwait(false);
        await _store.SaveAsync(snapshot, ct).ConfigureAwait(false);
    }

    private static RepairOutcome SemExecucao(RepairPlan plan, RepairStep passo, RepairStatus status, string motivo) =>
        new(plan.TargetId, Guid.Empty, passo.Kind, status, motivo);

    private static PrinterSnapshot SemContexto()
    {
        var alvoVazio = new PrinterTarget("-", null, null, PrinterProtocol.TcpRaw, null, null, null);
        return new PrinterSnapshot(
            Guid.Empty, DateTime.UnixEpoch, SnapshotOrigin.Manual, alvoVazio,
            null, null, null,
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            Snapshots.SnapshotStore.SchemaVersionAtual);
    }
}
