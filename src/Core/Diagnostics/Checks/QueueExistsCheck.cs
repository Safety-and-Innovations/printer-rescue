using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics.Checks;

/// <summary>
/// Checks whether the target print queue exists on the system: Pass when
/// GetQueueStateAsync confirms existence; Fail when the queue is missing.
/// </summary>
public sealed class QueueExistsCheck : IDiagnosticCheck
{
    /// <inheritdoc />
    public CheckId Id => CheckId.QueueExists;

    /// <inheritdoc />
    public async Task<CheckOutcome> RunAsync(IPrintSystemGateway gateway, PrinterTarget target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(target);

        var queue = await gateway.GetQueueStateAsync(target.Name, ct).ConfigureAwait(false);
        return queue.Exists
            ? new CheckOutcome(Id, CheckResult.Pass, Severity.Info,
                $"Print queue '{target.Name}' exists on the system.")
            : new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Print queue '{target.Name}' does not exist on the system.");
    }
}
