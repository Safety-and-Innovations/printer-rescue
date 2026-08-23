using PrinterRescue.Adapters.Windows.Winspool;
using Xunit;

namespace PrinterRescue.Tests.Windows;

/// <summary>
/// Testes da lógica PURA do adapter (mapeamentos e heurísticas) — rodam em qualquer SO.
/// As chamadas nativas reais ao winspool.drv só executam no Windows (guards CA1416).
/// </summary>
public sealed class MappersTests
{
    // ---- Protocolo a partir do nome da porta -------------------------------

    [Theory]
    [InlineData("USB001", PrinterRescue.Core.PrinterProtocol.Usb)]
    [InlineData("WSD-abc123", PrinterRescue.Core.PrinterProtocol.Wsd)]
    [InlineData("IP_192.168.0.40", PrinterRescue.Core.PrinterProtocol.TcpRaw)]
    [InlineData("PORTPROMPT:", PrinterRescue.Core.PrinterProtocol.TcpRaw)]
    [InlineData("", PrinterRescue.Core.PrinterProtocol.TcpRaw)]
    [InlineData(null, PrinterRescue.Core.PrinterProtocol.TcpRaw)]
    public void InferirProtocoloPorPrefixoDaPorta(string? porta, PrinterRescue.Core.PrinterProtocol esperado)
        => Assert.Equal(esperado, Mappers.InferirProtocolo(porta));

    // ---- Configuração de porta TCP derivada do nome ------------------------

    [Fact]
    public void DerivarPortConfigDeNomeIpPadrao9100()
    {
        var cfg = Mappers.DerivarPortConfig("IP_192.168.0.40");

        Assert.NotNull(cfg);
        Assert.Equal("IP_192.168.0.40", cfg.PortName);
        Assert.Equal("192.168.0.40", cfg.HostAddress);
        Assert.Equal(9100, cfg.PortNumber);
        Assert.Equal(PrinterRescue.Core.PrinterProtocol.TcpRaw, cfg.Protocol);
    }

    [Fact]
    public void DerivarPortConfigDeNomeIpComPortaExplicita()
    {
        var cfg = Mappers.DerivarPortConfig("IP_10.0.0.8_631");

        Assert.NotNull(cfg);
        Assert.Equal("10.0.0.8", cfg.HostAddress);
        Assert.Equal(631, cfg.PortNumber);
        Assert.Equal(PrinterRescue.Core.PrinterProtocol.Ipp, cfg.Protocol);
    }

    [Theory]
    [InlineData("USB001")]
    [InlineData("WSD-x")]
    [InlineData("não-é-ip")]
    public void DerivarPortConfigRetornaNullParaNaoTcp(string porta)
        => Assert.Null(Mappers.DerivarPortConfig(porta));

    // ---- Jobs presos -------------------------------------------------------

    [Fact]
    public void JobComFlagDeErroOuBloqueioEhPreso()
    {
        const uint JobStatusError = 0x00000002;
        const uint JobStatusBlocked = 0x00000400;
        const uint JobStatusRetained = 0x00000800;
        var agora = DateTime.UtcNow;

        Assert.True(Mappers.JobPreso(JobStatusError, submetidoEmUtc: agora.AddMinutes(-5), agoraUtc: agora));
        Assert.True(Mappers.JobPreso(JobStatusBlocked, submetidoEmUtc: agora.AddMinutes(-5), agoraUtc: agora));
        Assert.True(Mappers.JobPreso(JobStatusRetained, submetidoEmUtc: agora.AddMinutes(-5), agoraUtc: agora));
    }

    [Fact]
    public void JobAntigoSemStatusEhPreso()
    {
        var agora = DateTime.UtcNow;
        Assert.True(Mappers.JobPreso(status: 0, submetidoEmUtc: agora.AddHours(-25), agoraUtc: agora));
    }

    [Fact]
    public void JobNormalRecenteNaoEhPreso()
    {
        var agora = DateTime.UtcNow;
        const uint JobStatusPrinting = 0x00000004;
        Assert.False(Mappers.JobPreso(JobStatusPrinting, submetidoEmUtc: agora.AddMinutes(-1), agoraUtc: agora));
        Assert.False(Mappers.JobPreso(0, agora.AddMinutes(-30), agora));
    }

    // ---- Defaults do DevMode ----------------------------------------------

    [Fact]
    public void DevModePadraoDeFabricaNaoModificado()
    {
        // DM_OUT_BUFFER padrão: A4=9, cópias 1, monocromático=1, duplex simplex=1
        var d = Mappers.MapearDefaults(paperSize: 9, copies: 1, color: 1, duplex: 1);

        Assert.Equal("9", d["paperSize"]);
        Assert.Equal("1", d["copies"]);
        Assert.Equal("false", d["color"]);
        Assert.Equal("false", d["duplex"]);
    }

    [Fact]
    public void DevModeColoridoEDuplex()
    {
        var d = Mappers.MapearDefaults(paperSize: 1, copies: 3, color: 2, duplex: 2);

        Assert.Equal("true", d["color"]);
        Assert.Equal("true", d["duplex"]);
        Assert.Equal("3", d["copies"]);
    }

    [Fact]
    public void DevModeZeroProduzValoresNeutros()
    {
        var d = Mappers.MapearDefaults(0, 0, 0, 0);

        Assert.Equal("0", d["paperSize"]);
        Assert.Equal("1", d["copies"]); // nunca grava 0 cópias
        Assert.Equal("false", d["color"]);
        Assert.Equal("false", d["duplex"]);
    }

    // ---- Driver ------------------------------------------------------------

    [Fact]
    public void DetectarIppClassDriverPorNome()
    {
        Assert.True(Mappers.EhIppClassDriver("Microsoft IPP Class Driver"));
        Assert.True(Mappers.EhIppClassDriver("microsoft ipp class driver"));
        Assert.False(Mappers.EhIppClassDriver("HP Universal PCL6"));
        Assert.False(Mappers.EhIppClassDriver(""));
    }
}
