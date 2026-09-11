using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics.Checks;

/// <summary>
/// Checks whether exactly one installation of the printer exists on the system:
/// two or more entries with the same name indicate a corrupted duplicate install.
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

        var printers = await gateway.ListPrintersAsync(ct).ConfigureAwait(false);
        var occurrences = printers.Count(printer =>
            string.Equals(printer.Name, target.Name, StringComparison.OrdinalIgnoreCase));
        return occurrences switch
        {
            0 => new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Printer '{target.Name}' was not found in the system printer list."),
            1 => new CheckOutcome(Id, CheckResult.Pass, Severity.Info,
                $"Single installation of printer '{target.Name}' confirmed."),
            _ => new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Found {occurrences} duplicate installations of printer '{target.Name}'."),
        };
    }
}
