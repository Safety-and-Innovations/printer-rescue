using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics.Checks;

/// <summary>
/// Checks whether the target queue has stuck jobs: Pass when the queue is clear,
/// Warn when any stuck job is present.
/// </summary>
public sealed class QueueNotStuckCheck : IDiagnosticCheck
{
    /// <inheritdoc />
    public CheckId Id => CheckId.QueueNotStuck;

    /// <inheritdoc />
    public async Task<CheckOutcome> RunAsync(IPrintSystemGateway gateway, PrinterTarget target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(target);

        var queue = await gateway.GetQueueStateAsync(target.Name, ct).ConfigureAwait(false);
        return queue.StuckJobs == 0
            ? new CheckOutcome(Id, CheckResult.Pass, Severity.Info,
                $"Queue '{target.Name}' has no stuck jobs.")
            : new CheckOutcome(Id, CheckResult.Warn, Severity.Warning,
                $"{queue.StuckJobs} {(queue.StuckJobs == 1 ? "stuck job" : "stuck jobs")} in queue '{target.Name}'.");
    }
}
