using System.Net.Sockets;
using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Diagnostics.Checks;

/// <summary>
/// Testa a porta da impressora: abre conexão TCP em até 2 segundos.
/// Não se aplica a protocolos USB/WSD (sem endereço TCP para testar).
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
                $"Verificação de porta não se aplica ao protocolo {target.Protocol}.");
        }

        if (string.IsNullOrWhiteSpace(target.PortName))
        {
            return new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                "Impressora sem nome de porta configurada.");
        }

        var port = await gateway.GetPortAsync(target.PortName, ct).ConfigureAwait(false);
        if (port is null)
        {
            return new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Configuração da porta '{target.PortName}' não encontrada no sistema.");
        }

        var abriu = await TentaConectarAsync(port.HostAddress, port.PortNumber, ct).ConfigureAwait(false);
        return abriu
            ? new CheckOutcome(Id, CheckResult.Pass, Severity.Info,
                $"Porta {port.HostAddress}:{port.PortNumber} respondeu em menos de {Timeout.TotalSeconds:0}s.")
            : new CheckOutcome(Id, CheckResult.Fail, Severity.Error,
                $"Porta {port.HostAddress}:{port.PortNumber} não respondeu (timeout ou recusa).");
    }

    private static async Task<bool> TentaConectarAsync(string host, int porta, CancellationToken ct)
    {
        using var cliente = new TcpClient();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            await cliente.ConnectAsync(host, porta, cts.Token).ConfigureAwait(false);
            return cliente.Connected;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false; // estourou o timeout local, não o cancelamento do chamador
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
