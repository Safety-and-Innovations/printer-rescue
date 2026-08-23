using PrinterRescue.Cli;
using PrinterRescue.Core;
using Xunit;

namespace PrinterRescue.Core.Tests.Cli;

public sealed class ExitCodeMapperTests
{
    private static RepairOutcome Resultado(RepairStatus status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), RepairActionKind.ClearQueue, status, "-");

    // ---- Reparo -------------------------------------------------------------

    [Fact]
    public void PlanoVazioRetornaOk()
        => Assert.Equal(ExitCode.Ok, ExitCodeMapper.DeReparo([]));

    [Fact]
    public void AlgumAplicadoTemPrioridade()
        => Assert.Equal(ExitCode.RepairApplied, ExitCodeMapper.DeReparo(
            [Resultado(RepairStatus.Applied), Resultado(RepairStatus.Failed)]));

    [Fact]
    public void SemAplicadosComViolacaoDePolitica()
        => Assert.Equal(ExitCode.RepairBlockedByPolicy, ExitCodeMapper.DeReparo(
            [Resultado(RepairStatus.SkippedPolicyViolation)]));

    [Fact]
    public void SemAplicadosSemPoliticaComSkippedNoSnapshot()
        => Assert.Equal(ExitCode.SnapshotMissing, ExitCodeMapper.DeReparo(
            [Resultado(RepairStatus.SkippedNoSnapshot)]));

    [Fact]
    public void SoFalhasRetornaDiagnosticFailed()
        => Assert.Equal(ExitCode.DiagnosticFailed, ExitCodeMapper.DeReparo(
            [Resultado(RepairStatus.Failed), Resultado(RepairStatus.Failed)]));

    // ---- Diagnóstico --------------------------------------------------------

    [Fact]
    public void DiagnosticoSemFailRetornaOk()
        => Assert.Equal(ExitCode.Ok, ExitCodeMapper.DeDiagnostico(
            [new CheckOutcome(CheckId.PortOpen, CheckResult.Pass, Severity.Info, "-"),
             new CheckOutcome(CheckId.QueueNotStuck, CheckResult.Warn, Severity.Warning, "-")]));

    [Fact]
    public void DiagnosticoComFailRetornaDiagnosticFailed()
        => Assert.Equal(ExitCode.DiagnosticFailed, ExitCodeMapper.DeDiagnostico(
            [new CheckOutcome(CheckId.PortOpen, CheckResult.Fail, Severity.Error, "-")]));

    // ---- Formatação ---------------------------------------------------------

    [Fact]
    public void FormatarCheckIncluiIdResultadoEDetalhe()
    {
        var linha = OutputFormatter.Check(
            new CheckOutcome(CheckId.QueueNotStuck, CheckResult.Warn, Severity.Warning, "3 trabalhos presos"));

        Assert.Contains("QueueNotStuck", linha);
        Assert.Contains("WARN", linha);
        Assert.Contains("3 trabalhos presos", linha);
    }

    [Fact]
    public void FormatarPlanoListaPassosNumerados()
    {
        var linhas = OutputFormatter.Plano(
        [
            new RepairStep(RepairActionKind.ClearQueue, "Limpar fila", Destructive: false, RequiresElevation: false),
            new RepairStep(RepairActionKind.RemoveBrokenInstall, "Remover quebrada", Destructive: true, RequiresElevation: true),
        ]);

        Assert.Equal(2, linhas.Count);
        Assert.StartsWith("1.", linhas[0]);
        Assert.Contains("[destrutivo]", linhas[1]);
        Assert.Contains("[elevação]", linhas[1]);
    }
}
