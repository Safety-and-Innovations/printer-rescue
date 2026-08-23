using PrinterRescue.Core.Policy;
using Xunit;

namespace PrinterRescue.Core.Tests.Policy;

/// <summary>Testes da guarda de conformidade — REGRA Nº 1 (nunca baixar, hospedar ou distribuir driver).</summary>
public sealed class PolicyGuardTests
{
    private readonly PolicyGuard _guard = new();

    [Theory]
    [InlineData(RepairActionKind.RestorePort)]
    [InlineData(RepairActionKind.ClearQueue)]
    [InlineData(RepairActionKind.RestartSpooler)]
    [InlineData(RepairActionKind.RestorePermissions)]
    [InlineData(RepairActionKind.RestoreDefaults)]
    [InlineData(RepairActionKind.ReinstallWithIppClassDriver)]
    [InlineData(RepairActionKind.ReinstallFromDriverStore)]
    [InlineData(RepairActionKind.RemoveBrokenInstall)]
    public void EvaluateComSnapshotEPadraoSaoPermitidos(RepairActionKind kind)
    {
        var passo = new RepairStep(kind, "Executar procedimento local de manutencao da impressora.", Destructive: false, RequiresElevation: false);

        var decisao = _guard.Evaluate(passo, CriarSnapshot());

        Assert.True(decisao.Allowed);
        Assert.False(string.IsNullOrWhiteSpace(decisao.Reason));
    }

    [Theory]
    [InlineData("Baixar driver do site do fabricante")]
    [InlineData("Fazer DOWNLOAD do pacote de driver completo")]
    [InlineData("Hospedar driver em servidor interno da rede")]
    [InlineData("Distribuir driver para as estacoes de trabalho")]
    [InlineData("Reinstalar usando catalogo de driver da web")]
    [InlineData("Instalar driver externo assinado pelo fabricante")]
    [InlineData("Rotina que vai Baixar e Hospedar driver no compartilhamento")]
    public void EvaluateNegaDescricaoComTermoDeDistribuicaoDeDriver(string descricao)
    {
        var passo = new RepairStep(RepairActionKind.ReinstallFromDriverStore, descricao, Destructive: true, RequiresElevation: true);

        var decisao = _guard.Evaluate(passo, CriarSnapshot());

        Assert.False(decisao.Allowed);
        Assert.Contains("REGRA", decisao.Reason);
    }

    [Fact]
    public void EvaluateNegaIndependenteDoKindQuandoDescricaoEVetor()
    {
        // Kind desconhecido fora do enum nao existe em C#; o vetor principal de violacao e a Description.
        var passo = new RepairStep(
            RepairActionKind.RestoreDefaults,
            "Publicar driver: hospedar driver em repositorio online e distribuir driver as filiais.",
            Destructive: false,
            RequiresElevation: false);

        var decisao = _guard.Evaluate(passo, CriarSnapshot());

        Assert.False(decisao.Allowed);
    }

    [Fact]
    public void EvaluateNegacaoInformaOTermoDetectado()
    {
        var passo = new RepairStep(RepairActionKind.ClearQueue, "Rotina que vai baixar driver antes de limpar a fila.", Destructive: false, RequiresElevation: false);

        var decisao = _guard.Evaluate(passo, CriarSnapshot());

        Assert.False(decisao.Allowed);
        Assert.Contains("baixar", decisao.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateNaoDependeDoSnapshotParaDecidir()
    {
        var passoSeguro = new RepairStep(RepairActionKind.ClearQueue, "Limpar a fila de impressao.", Destructive: false, RequiresElevation: false);
        var passoProibido = new RepairStep(RepairActionKind.ClearQueue, "Baixar driver antes de limpar a fila.", Destructive: false, RequiresElevation: false);

        var permitido = _guard.Evaluate(passoSeguro, CriarSnapshot());
        var negado = _guard.Evaluate(passoProibido, CriarSnapshot());

        Assert.True(permitido.Allowed);
        Assert.False(negado.Allowed);
    }

    private static PrinterSnapshot CriarSnapshot()
    {
        var alvo = TestTargets.Tcp();
        return new PrinterSnapshot(
            Id: Guid.NewGuid(),
            CreatedAtUtc: DateTime.UtcNow,
            Origin: SnapshotOrigin.PreRepair,
            Target: alvo,
            Port: new PortConfig("IP_192.168.0.40", "192.168.0.40", 9100, PrinterProtocol.TcpRaw),
            Queue: new QueueState(alvo.Name, Exists: true, StuckJobs: 0, DefaultPaperSize: "A4", CopiesDefault: 1, ColorDefault: false, DuplexDefault: true),
            Driver: new DriverInfo("HP Universal PCL6", "3.12.0.0", InfName: null, PresentInDriverStore: true, IsIppClassDriver: false),
            Permissions: new Dictionary<string, string>(),
            Defaults: new Dictionary<string, string>(),
            SchemaVersion: "1.0");
    }
}
