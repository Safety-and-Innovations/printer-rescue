using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics.Checks;

/// <summary>
/// Verifica se o driver da impressora existe e está presente no Driver Store local.
/// Não se aplica quando o alvo não tem nome de driver configurado.
/// </summary>
public sealed class DriverPresentCheck : IDiagnosticCheck
{
    /// <inheritdoc />
    public CheckId Id => CheckId.DriverPresent;

    /// <inheritdoc />
    public async Task<CheckOutcome> RunAsync(IPrintSystemGateway gateway, PrinterTarget target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(target);

        if (string.IsNullOrWhiteSpace(target.DriverName))
        {
            return new CheckOutcome(Id, CheckResult.NotApplicable, Severity.Info,
                "Verificação de driver não se aplica: impressora sem nome de driver configurado.");
        }

        var driver = await gateway.GetDriverInfoAsync(target.DriverName, ct).ConfigureAwait(false);
        if (driver is null)
        {
            return new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Driver '{target.DriverName}' não encontrado no sistema.");
        }

        if (!driver.PresentInDriverStore)
        {
            return new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Driver '{target.DriverName}' em uso não está presente no Driver Store local.");
        }

        return new CheckOutcome(Id, CheckResult.Pass, Severity.Info,
            $"Driver '{driver.Name}' versão {driver.Version} presente no Driver Store local.");
    }
}
