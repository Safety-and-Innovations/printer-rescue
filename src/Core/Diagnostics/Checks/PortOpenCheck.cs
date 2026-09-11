using System.Net.Sockets;
using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics.Checks;

/// <summary>
/// Tests the printer port: opens a TCP connection within 2 seconds.
/// Does not apply to USB/WSD protocols (no TCP address to probe).
/// </summary>
public sealed class PortOpenCheck : IDiagnosticCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public CheckId Id => CheckId.PortOpen;

    /// <inheritdoc />
    public async Task<CheckOutcome> RunAsync(IPrintSystemGateway gateway, PrinterTarget target, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(target);

        if (target.Protocol is PrinterProtocol.Usb or PrinterProtocol.Wsd)
        {
            return new CheckOutcome(Id, CheckResult.NotApplicable, Severity.Info,
                $"Port check does not apply to protocol {target.Protocol}.");
        }

        if (string.IsNullOrWhiteSpace(target.PortName))
        {
            return new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                "Printer has no configured port name.");
        }

        var port = await gateway.GetPortAsync(target.PortName, ct).ConfigureAwait(false);
        if (port is null)
        {
            return new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Port configuration '{target.PortName}' was not found on the system.");
        }

        var opened = await TryConnectAsync(port.HostAddress, port.PortNumber, ct).ConfigureAwait(false);
        return opened
            ? new CheckOutcome(Id, CheckResult.Pass, Severity.Info,
                $"Port {port.HostAddress}:{port.PortNumber} responded in under {Timeout.TotalSeconds:0}s.")
            : new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Port {port.HostAddress}:{port.PortNumber} did not respond (timeout or refusal).");
    }

    private static async Task<bool> TryConnectAsync(string host, int port, CancellationToken ct)
    {
        using var client = new TcpClient();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false; // hit the local timeout, not the caller's cancellation
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
