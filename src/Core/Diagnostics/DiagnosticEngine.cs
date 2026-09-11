using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics;

/// <summary>
/// Runs the deterministic check sequence in the contractual CheckId order,
/// applies spooler gating (spooler Fail implies remaining checks NotApplicable),
/// and attaches the repair plan produced by the IRepairPlanner with the latest valid snapshot.
/// </summary>
public sealed class DiagnosticEngine : IDiagnosticEngine
{
    /// <summary>Contractual execution order: the enum declaration order itself.</summary>
    private static readonly CheckId[] ContractualOrder =
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

        var startedUtc = DateTime.UtcNow;

        var byId = new Dictionary<CheckId, IDiagnosticCheck>();
        foreach (var check in _checks)
        {
            if (!byId.TryAdd(check.Id, check))
            {
                throw new InvalidOperationException($"Duplicate check injected: {check.Id}.");
            }
        }

        var results = new List<CheckOutcome>(ContractualOrder.Length);
        var spoolerFailed = false;
        foreach (var id in ContractualOrder)
        {
            ct.ThrowIfCancellationRequested();

            if (spoolerFailed && id != CheckId.SpoolerRunning)
            {
                results.Add(new CheckOutcome(
                    id,
                    CheckResult.NotApplicable,
                    Severity.Info,
                    "Not applicable: diagnostics aborted at the spooler check."));
                continue;
            }

            if (!byId.TryGetValue(id, out var check))
            {
                // A missing check does not block the report; it is recorded as not applicable.
                results.Add(new CheckOutcome(id, CheckResult.NotApplicable, Severity.Info, "Check not registered in the engine."));
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
                // A check exception never takes down diagnostics: it becomes Fail/Error.
                outcome = new CheckOutcome(id, CheckResult.Fail, Severity.Error,
                    $"Unexpected failure running check {id}: {ex.Message}");
            }

            results.Add(outcome);

            if (id == CheckId.SpoolerRunning && outcome.Result == CheckResult.Fail)
            {
                spoolerFailed = true;
            }
        }

        var partialReport = new DiagnosticReport(
            TargetId(target),
            startedUtc,
            DateTime.UtcNow,
            results,
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
                // A store with invalid data does not take down diagnostics; proceed without a snapshot.
            }
        }

        var plan = _planner.PlanRepairs(partialReport, lastGood);

        return partialReport with { Plan = plan };
    }

    /// <summary>
    /// Stable identifier for the target: derived from the printer name (deterministic
    /// within the session), since PrinterTarget carries no Guid of its own in the frozen contract.
    /// </summary>
    private static Guid TargetId(PrinterTarget target)
        => new(HashToGuidBytes(target.Name));

    private static byte[] HashToGuidBytes(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        var guid = new byte[16];
        Array.Copy(hash, guid, 16);
        return guid;
    }
}
