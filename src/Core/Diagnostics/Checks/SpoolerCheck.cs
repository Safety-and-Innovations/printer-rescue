using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics.Checks;

/// <summary>
/// Verifica se o serviço de spooler está acessível.
/// Pass quando o gateway responde; Fail em qualquer falha de acesso
/// (os demais checks ficam NotApplicable — gating feito pelo engine).
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
            return new CheckOutcome(Id, CheckResult.Pass, Severity.Info, "Spooler acessível.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Check de diagnóstico nunca propaga falha do ambiente: converte em Fail.
            return new CheckOutcome(Id, CheckResult.Fail, Severity.Error, $"Spooler inacessível: {ex.Message}");
        }
    }
}
