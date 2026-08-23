using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics.Checks;

/// <summary>
/// Verifica se a fila de impressão do alvo existe no sistema: Pass quando
/// GetQueueStateAsync confirma existência; Fail quando a fila está ausente.
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

        var fila = await gateway.GetQueueStateAsync(target.Name, ct).ConfigureAwait(false);
        return fila.Exists
            ? new CheckOutcome(Id, CheckResult.Pass, Severity.Info,
                $"Fila de impressão '{target.Name}' existe no sistema.")
            : new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Fila de impressão '{target.Name}' não existe no sistema.");
    }
}
