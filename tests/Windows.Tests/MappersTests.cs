using PrinterRescue.Adapters.Windows.Winspool;
using Xunit;

namespace PrinterRescue.Tests.Windows;

/// <summary>
/// Tests for the adapter's PURE logic (mappings and heuristics) — run on any OS.
/// Real native winspool.drv calls only run on Windows (CA1416 guards).
/// </summary>
public sealed class MappersTests
{
    // ---- Protocol from the port-name prefix -------------------------------

    [Theory]
    [InlineData("USB001", PrinterRescue.Core.PrinterProtocol.Usb)]
    [InlineData("WSD-abc123", PrinterRescue.Core.PrinterProtocol.Wsd)]
    [InlineData("IP_192.168.0.40", PrinterRescue.Core.PrinterProtocol.TcpRaw)]
    [InlineData("PORTPROMPT:", PrinterRescue.Core.PrinterProtocol.TcpRaw)]
    [InlineData("", PrinterRescue.Core.PrinterProtocol.TcpRaw)]
    [InlineData(null, PrinterRescue.Core.PrinterProtocol.TcpRaw)]
    public void InferProtocolFromPortPrefix(string? port, PrinterRescue.Core.PrinterProtocol expected)
        => Assert.Equal(expected, Mappers.InferProtocol(port));

    // ---- TCP port config derived from the name ----------------------------

    [Fact]
    public void DerivePortConfigFromDefaultIpName9100()
    {
        var cfg = Mappers.DerivePortConfig("IP_192.168.0.40");

        Assert.NotNull(cfg);
        Assert.Equal("IP_192.168.0.40", cfg.PortName);
        Assert.Equal("192.168.0.40", cfg.HostAddress);
        Assert.Equal(9100, cfg.PortNumber);
        Assert.Equal(PrinterRescue.Core.PrinterProtocol.TcpRaw, cfg.Protocol);
    }

    [Fact]
    public void DerivePortConfigFromIpNameWithExplicitPort()
    {
        var cfg = Mappers.DerivePortConfig("IP_10.0.0.8_631");

        Assert.NotNull(cfg);
        Assert.Equal("10.0.0.8", cfg.HostAddress);
        Assert.Equal(631, cfg.PortNumber);
        Assert.Equal(PrinterRescue.Core.PrinterProtocol.Ipp, cfg.Protocol);
    }

    [Theory]
    [InlineData("USB001")]
    [InlineData("WSD-x")]
    [InlineData("not-an-ip")]
    public void DerivePortConfigReturnsNullForNonTcp(string port)
        => Assert.Null(Mappers.DerivePortConfig(port));

    // ---- Stuck jobs --------------------------------------------------------

    [Fact]
    public void JobWithErrorOrBlockedFlagIsStuck()
    {
        const uint JobStatusError = 0x00000002;
        const uint JobStatusBlocked = 0x00000400;
        const uint JobStatusRetained = 0x00000800;
        var now = DateTime.UtcNow;

        Assert.True(Mappers.IsJobStuck(JobStatusError, submittedUtc: now.AddMinutes(-5), nowUtc: now));
        Assert.True(Mappers.IsJobStuck(JobStatusBlocked, submittedUtc: now.AddMinutes(-5), nowUtc: now));
        Assert.True(Mappers.IsJobStuck(JobStatusRetained, submittedUtc: now.AddMinutes(-5), nowUtc: now));
    }

    [Fact]
    public void OldJobWithoutStatusIsStuck()
    {
        var now = DateTime.UtcNow;
        Assert.True(Mappers.IsJobStuck(status: 0, submittedUtc: now.AddHours(-25), nowUtc: now));
    }

    [Fact]
    public void RecentNormalJobIsNotStuck()
    {
        var now = DateTime.UtcNow;
        const uint JobStatusPrinting = 0x00000004;
        Assert.False(Mappers.IsJobStuck(JobStatusPrinting, submittedUtc: now.AddMinutes(-1), nowUtc: now));
        Assert.False(Mappers.IsJobStuck(0, now.AddMinutes(-30), now));
    }

    // ---- DevMode defaults --------------------------------------------------

    [Fact]
    public void DevModeFactoryDefaultsUnmodified()
    {
        // Default DM_OUT_BUFFER: A4=9, 1 copy, monochrome=1, simplex duplex=1
        var d = Mappers.MapDefaults(paperSize: 9, copies: 1, color: 1, duplex: 1);

        Assert.Equal("9", d["paperSize"]);
        Assert.Equal("1", d["copies"]);
        Assert.Equal("false", d["color"]);
        Assert.Equal("false", d["duplex"]);
    }

    [Fact]
    public void DevModeColorAndDuplex()
    {
        var d = Mappers.MapDefaults(paperSize: 1, copies: 3, color: 2, duplex: 2);

        Assert.Equal("true", d["color"]);
        Assert.Equal("true", d["duplex"]);
        Assert.Equal("3", d["copies"]);
    }

    [Fact]
    public void DevModeZeroProducesNeutralValues()
    {
        var d = Mappers.MapDefaults(0, 0, 0, 0);

        Assert.Equal("0", d["paperSize"]);
        Assert.Equal("1", d["copies"]); // never records 0 copies
        Assert.Equal("false", d["color"]);
        Assert.Equal("false", d["duplex"]);
    }

    // ---- Driver ------------------------------------------------------------

    [Fact]
    public void DetectIppClassDriverByName()
    {
        Assert.True(Mappers.IsIppClassDriver("Microsoft IPP Class Driver"));
        Assert.True(Mappers.IsIppClassDriver("microsoft ipp class driver"));
        Assert.False(Mappers.IsIppClassDriver("HP Universal PCL6"));
        Assert.False(Mappers.IsIppClassDriver(""));
    }
}
