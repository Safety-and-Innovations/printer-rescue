using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics.Checks;

/// <summary>
/// Verifica se há trabalhos presos na fila do alvo: Pass com fila livre,
/// Warn quando existe qualquer trabalho travado na fila.
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

        var fila = await gateway.GetQueueStateAsync(target.Name, ct).ConfigureAwait(false);
        return fila.StuckJobs == 0
            ? new CheckOutcome(Id, CheckResult.Pass, Severity.Info,
                $"Fila '{target.Name}' sem trabalhos presos.")
            : new CheckOutcome(Id, CheckResult.Warn, Severity.Warning,
                $"{fila.StuckJobs} {(fila.StuckJobs == 1 ? "trabalho preso" : "trabalhos presos")} na fila '{target.Name}'.");
    }
}
