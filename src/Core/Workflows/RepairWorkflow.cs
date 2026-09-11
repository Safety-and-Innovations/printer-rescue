using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Workflows;

/// <summary>
/// Runs a repair plan step by step with the product guarantees:
/// policy guard first (RULE #1), mandatory elevation for
/// steps that require it, a pre-repair snapshot before the first destructive step,
/// and a single post-repair snapshot if anything was applied.
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

        var results = new List<RepairOutcome>();
        var preCaptureDone = false;
        var appliedAny = false;

        foreach (var step in plan.Steps)
        {
            ct.ThrowIfCancellationRequested();

            // 1. Policy first: nothing runs without passing RULE #1.
            var decision = _guard.Evaluate(step, lastGood ?? EmptyContext());
            if (!decision.Allowed)
            {
                results.Add(WithoutExecution(plan, step, RepairStatus.SkippedPolicyViolation, decision.Reason));
                continue;
            }

            // 2. Elevation: a step requiring admin without it fails before running.
            if (step.RequiresElevation && !_executor.IsElevated())
            {
                results.Add(WithoutExecution(plan, step, RepairStatus.Failed,
                    $"Step '{step.Kind}' requires administrative privilege and the process is not elevated."));
                continue;
            }

            // 3. Destructive steps require a valid snapshot for rollback.
            if (step.Destructive && lastGood is null)
            {
                results.Add(WithoutExecution(plan, step, RepairStatus.SkippedNoSnapshot,
                    $"[SkippedNoSnapshot] Destructive step '{step.Kind}' requires a prior valid snapshot."));
                continue;
            }

            // 4. Before the first destructive step: record the pre-repair snapshot exactly once.
            if (step.Destructive && !preCaptureDone && lastGood is not null)
            {
                await CaptureAsync(lastGood.Target, SnapshotOrigin.PreRepair, ct).ConfigureAwait(false);
                preCaptureDone = true;
            }

            var context = lastGood ?? EmptyContext();
            var outcome = await _executor.ExecuteAsync(step, context, ct).ConfigureAwait(false);
            appliedAny |= outcome.Status == RepairStatus.Applied;
            results.Add(outcome);
        }

        // 5. Post-repair: record the new state if anything was applied.
        if (appliedAny && lastGood is not null)
        {
            await CaptureAsync(lastGood.Target, SnapshotOrigin.PostRepair, ct).ConfigureAwait(false);
        }

        return results;
    }

    private async Task CaptureAsync(PrinterTarget target, SnapshotOrigin origin, CancellationToken ct)
    {
        if (_store is null || _capture is null)
        {
            return;
        }

        var snapshot = await _capture.CaptureAsync(target, origin, ct).ConfigureAwait(false);
        await _store.SaveAsync(snapshot, ct).ConfigureAwait(false);
    }

    private static RepairOutcome WithoutExecution(RepairPlan plan, RepairStep step, RepairStatus status, string reason) =>
        new(plan.TargetId, Guid.Empty, step.Kind, status, reason);

    private static PrinterSnapshot EmptyContext()
    {
        var emptyTarget = new PrinterTarget("-", null, null, PrinterProtocol.TcpRaw, null, null, null);
        return new PrinterSnapshot(
            Guid.Empty, DateTime.UnixEpoch, SnapshotOrigin.Manual, emptyTarget,
            null, null, null,
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            Snapshots.SnapshotStore.CurrentSchemaVersion);
    }
}
