using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics.Checks;

/// <summary>
/// Checks whether the printer driver exists and is present in the local driver store.
/// Does not apply when the target has no configured driver name.
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
                "Driver check does not apply: printer has no configured driver name.");
        }

        var driver = await gateway.GetDriverInfoAsync(target.DriverName, ct).ConfigureAwait(false);
        if (driver is null)
        {
            return new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Driver '{target.DriverName}' was not found on the system.");
        }

        if (!driver.PresentInDriverStore)
        {
            return new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Driver '{target.DriverName}' in use is not present in the local driver store.");
        }

        return new CheckOutcome(Id, CheckResult.Pass, Severity.Info,
            $"Driver '{driver.Name}' version {driver.Version} is present in the local driver store.");
    }
}
