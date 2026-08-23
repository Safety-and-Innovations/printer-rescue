using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics.Checks;

/// <summary>
/// Verifica se existe exatamente uma instalação da impressora no sistema:
/// duas ou mais entradas com o mesmo nome indicam instalação duplicada corrompida.
/// </summary>
public sealed class NoDuplicateInstallCheck : IDiagnosticCheck
{
    /// <inheritdoc />
    public CheckId Id => CheckId.NoDuplicateInstall;

    /// <inheritdoc />
    public async Task<CheckOutcome> RunAsync(IPrintSystemGateway gateway, PrinterTarget target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(target);

        var impressoras = await gateway.ListPrintersAsync(ct).ConfigureAwait(false);
        var ocorrencias = impressoras.Count(impressora =>
            string.Equals(impressora.Name, target.Name, StringComparison.OrdinalIgnoreCase));
        return ocorrencias switch
        {
            0 => new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Impressora '{target.Name}' não encontrada na lista de impressoras do sistema."),
            1 => new CheckOutcome(Id, CheckResult.Pass, Severity.Info,
                $"Instalação única da impressora '{target.Name}' confirmada."),
            _ => new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Encontradas {ocorrencias} instalações duplicadas da impressora '{target.Name}'."),
        };
    }
}
