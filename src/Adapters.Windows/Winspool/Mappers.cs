using System.Runtime.InteropServices;
using PrinterRescue.Core;

namespace PrinterRescue.Adapters.Windows.Winspool;

/// <summary>
/// Lógica pura de mapeamento entre estruturas nativas do winspool.drv e os
/// modelos do Core. Métodos estáticos, determinísticos e testáveis em qualquer SO —
/// o P/Invoke real (só executa no Windows) fica em WinspoolNative.
/// </summary>
public static class Mappers
{
    // Flags de JOB_STATUS que caracterizam trabalho preso/bloqueado.
    private const uint JobStatusError = 0x0000_0002;
    private const uint JobStatusBlockedDevQueue = 0x0000_0400;
    private const uint JobStatusRetained = 0x0000_0800;
    private static readonly TimeSpan IdadePreso = TimeSpan.FromHours(24);

    /// <summary>Infere o protocolo pelo prefixo do nome da porta (convenções do Windows).</summary>
    public static PrinterProtocol InferirProtocolo(string? portName)
    {
        if (string.IsNullOrEmpty(portName))
        {
            return PrinterProtocol.TcpRaw;
        }

        if (portName.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
        {
            return PrinterProtocol.Usb;
        }

        if (portName.StartsWith("WSD", StringComparison.OrdinalIgnoreCase))
        {
            return PrinterProtocol.Wsd;
        }

        return PrinterProtocol.TcpRaw;
    }

    /// <summary>
    /// Deriva a configuração de porta TCP a partir do nome padrão do Windows:
    /// "IP_host" usa 9100; "IP_host_porta" usa a porta explícita (e protocolo IPP para 631).
    /// Retorna null quando o nome não é uma porta TCP/IP.
    /// </summary>
    public static PortConfig? DerivarPortConfig(string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName) || !portName.StartsWith("IP_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var partes = portName[3..].Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0)
        {
            return null;
        }

        var host = partes[0];
        var numero = partes.Length > 1 && int.TryParse(partes[1], out var p) ? p : 9100;
        var protocolo = numero == 631 ? PrinterProtocol.Ipp : PrinterProtocol.TcpRaw;

        return new PortConfig(portName, host, numero, protocolo);
    }

    /// <summary>
    /// Heurística de trabalho preso: flag de erro/bloqueio/retenção, ou qualquer
    /// trabalho com mais de 24 h na fila sem status ativo de impressão.
    /// </summary>
    public static bool JobPreso(uint status, DateTime submetidoEmUtc, DateTime agoraUtc)
    {
        const uint JobStatusPrinting = 0x0000_0004;
        if ((status & (JobStatusError | JobStatusBlockedDevQueue | JobStatusRetained)) != 0)
        {
            return true;
        }

        if (agoraUtc - submetidoEmUtc > IdadePreso && (status & JobStatusPrinting) == 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Mapeia os campos relevantes do DEVMODE para os padrões gravados no snapshot.
    /// Valores zero/ausentes viram neutros (nunca grava "0 cópias").
    /// </summary>
    public static IReadOnlyDictionary<string, string> MapearDefaults(int paperSize, int copies, int color, int duplex)
    {
        var d = new Dictionary<string, string>
        {
            ["paperSize"] = paperSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["copies"] = Math.Max(copies, 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["color"] = (color == 2).ToString().ToLowerInvariant(),
            ["duplex"] = (duplex == 2).ToString().ToLowerInvariant(),
        };
        return d;
    }

    /// <summary>Detecta o driver de classe IPP nativo pelo nome (REGRA Nº 1: é o preferido na reinstalação).</summary>
    public static bool EhIppClassDriver(string driverName)
        => driverName.Contains("ipp class driver", StringComparison.OrdinalIgnoreCase);
}
