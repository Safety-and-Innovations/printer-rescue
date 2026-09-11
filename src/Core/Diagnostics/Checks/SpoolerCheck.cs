using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics.Checks;

/// <summary>
/// Checks whether the spooler service is reachable.
/// Pass when the gateway responds; Fail on any access failure
/// (remaining checks become NotApplicable — gating done by the engine).
/// </summary>
public sealed class SpoolerCheck : IDiagnosticCheck
{
    /// <inheritdoc />
    public CheckId Id => CheckId.SpoolerRunning;

    /// <inheritdoc />
    public async Task<CheckOutcome> RunAsync(IPrintSystemGateway gateway, PrinterTarget target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(target);

        try
        {
            _ = await gateway.ListPrintersAsync(ct).ConfigureAwait(false);
            return new CheckOutcome(Id, CheckResult.Pass, Severity.Info, "Spooler reachable.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A diagnostic check never propagates environment failures: converts them to Fail.
            return new CheckOutcome(Id, CheckResult.Fail, Severity.Error, $"Spooler unreachable: {ex.Message}");
        }
    }
}
