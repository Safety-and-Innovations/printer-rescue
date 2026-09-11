using System.Runtime.InteropServices;
using PrinterRescue.Core;

namespace PrinterRescue.Adapters.Windows.Winspool;

/// <summary>
/// Pure mapping logic between the native winspool.drv structures and the
/// Core models. Static, deterministic methods testable on any OS —
/// the real P/Invoke (Windows-only at runtime) lives in WinspoolNative.
/// </summary>
public static class Mappers
{
    // JOB_STATUS flags that mark a stuck/blocked job.
    private const uint JobStatusError = 0x0000_0002;
    private const uint JobStatusBlockedDevQueue = 0x0000_0400;
    private const uint JobStatusRetained = 0x0000_0800;
    private static readonly TimeSpan StuckAge = TimeSpan.FromHours(24);

    /// <summary>Infers the protocol from the port-name prefix (Windows conventions).</summary>
    public static PrinterProtocol InferProtocol(string? portName)
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
    /// Derives the TCP port configuration from the default Windows name:
    /// "IP_host" uses 9100; "IP_host_port" uses the explicit port (and IPP for 631).
    /// Returns null when the name is not a TCP/IP port.
    /// </summary>
    public static PortConfig? DerivePortConfig(string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName) || !portName.StartsWith("IP_", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = portName[3..].Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var host = parts[0];
        var number = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 9100;
        var protocol = number == 631 ? PrinterProtocol.Ipp : PrinterProtocol.TcpRaw;

        return new PortConfig(portName, host, number, protocol);
    }

    /// <summary>
    /// Stuck-job heuristic: error/blocked/retained flag, or any job
    /// older than 24 h in the queue with no active printing status.
    /// </summary>
    public static bool IsJobStuck(uint status, DateTime submittedUtc, DateTime nowUtc)
    {
        const uint JobStatusPrinting = 0x0000_0004;
        if ((status & (JobStatusError | JobStatusBlockedDevQueue | JobStatusRetained)) != 0)
        {
            return true;
        }

        if (nowUtc - submittedUtc > StuckAge && (status & JobStatusPrinting) == 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Maps the relevant DEVMODE fields to the defaults recorded in the snapshot.
    /// Zero/missing values become neutral (never records "0 copies").
    /// </summary>
    public static IReadOnlyDictionary<string, string> MapDefaults(int paperSize, int copies, int color, int duplex)
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

    /// <summary>Detects the native IPP class driver by name (RULE #1: it is the reinstall favorite).</summary>
    public static bool IsIppClassDriver(string driverName)
        => driverName.Contains("ipp class driver", StringComparison.OrdinalIgnoreCase);
}
