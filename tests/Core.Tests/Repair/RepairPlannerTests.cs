using PrinterRescue.Core.Interfaces;
using PrinterRescue.Core.Repair;
using Xunit;

namespace PrinterRescue.Core.Tests.Repair;

/// <summary>Testes do gerador de planos de reparo: ordem contratual, snapshot e conservadorismo.</summary>
public sealed class RepairPlannerTests
{
    private readonly RepairPlanner _planner = new();

    [Fact]
    public void FilaComWarnGeraClearQueueAntesDeRestartSpooler()
    {
        var report = CriarReport(fila: CheckResult.Warn);

        var plano = _planner.PlanRepairs(report, lastGood: null);

        Assert.Contains(plano.Steps, static s => s.Kind == RepairActionKind.ClearQueue);
        Assert.Contains(plano.Steps, static s => s.Kind == RepairActionKind.RestartSpooler);
        Assert.True(Indice(plano, RepairActionKind.ClearQueue) < Indice(plano, RepairActionKind.RestartSpooler));
    }

    [Fact]
    public void PortaEmFailComSnapshotGeraRestorePortConsistente()
    {
        var portaBoa = new PortConfig("IP_192.168.0.40", "192.168.0.40", 9100, PrinterProtocol.TcpRaw);
        var snapshot = CriarSnapshot(porta: portaBoa);
        var report = CriarReport(porta: CheckResult.Fail);

        var plano = _planner.PlanRepairs(report, snapshot);

        var passoPorta = Assert.Single(plano.Steps, static s => s.Kind == RepairActionKind.RestorePort);
        Assert.False(passoPorta.Destructive);
        Assert.True(passoPorta.RequiresElevation);
        // Consistência com o snapshot: nome e endereço da porta boa aparecem na descrição.
        Assert.Contains(portaBoa.PortName, passoPorta.Description, StringComparison.Ordinal);
        Assert.Contains(portaBoa.HostAddress, passoPorta.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverEmFailSemIppClassDriverPlanejaReinstallComIppAntesDeDriverStore()
    {
        var driverRuim = new DriverInfo("HP Universal PCL6", "3.12.0.0", InfName: null, PresentInDriverStore: true, IsIppClassDriver: false);
        var snapshot = CriarSnapshot(driver: driverRuim);
        var report = CriarReport(driver: CheckResult.Fail);

        var plano = _planner.PlanRepairs(report, snapshot);

        Assert.Contains(plano.Steps, static s => s.Kind == RepairActionKind.ReinstallWithIppClassDriver);
        Assert.Contains(plano.Steps, static s => s.Kind == RepairActionKind.ReinstallFromDriverStore);
        Assert.True(
            Indice(plano, RepairActionKind.ReinstallWithIppClassDriver) < Indice(plano, RepairActionKind.ReinstallFromDriverStore));
    }

    [Fact]
    public void SemSnapshotNenhumPassoDestrutivoEntraNoPlano()
    {
        var report = CriarReport(porta: CheckResult.Fail, driver: CheckResult.Fail);

        var plano = _planner.PlanRepairs(report, lastGood: null);

        Assert.DoesNotContain(plano.Steps, static s => s.Destructive);
        Assert.DoesNotContain(plano.Steps, static s => s.Kind == RepairActionKind.RemoveBrokenInstall);
        Assert.DoesNotContain(plano.Steps, static s => s.Kind == RepairActionKind.ReinstallWithIppClassDriver);
        Assert.DoesNotContain(plano.Steps, static s => s.Kind == RepairActionKind.ReinstallFromDriverStore);
        // Passos destrutivos potenciais ficam registrados como SkippedNoSnapshot.
        Assert.Contains("SkippedNoSnapshot", plano.Steps[^1].Description, StringComparison.Ordinal);
    }

    [Fact]
    public void TudoPassProduzPlanoVazio()
    {
        var report = CriarReport();

        var plano = _planner.PlanRepairs(report, lastGood: null);

        Assert.Empty(plano.Steps);
        Assert.False(plano.RequiresElevation);
    }

    [Fact]
    public void OrdemGlobalEContratual()
    {
        var report = CriarReport(fila: CheckResult.Warn, porta: CheckResult.Fail, driver: CheckResult.Fail);
        var snapshot = CriarSnapshot(porta: new PortConfig("IP_192.168.0.40", "192.168.0.40", 9100, PrinterProtocol.TcpRaw));

        var plano = _planner.PlanRepairs(report, snapshot);

        var kinds = plano.Steps.Select(static s => s.Kind).ToList();
        Assert.True(kinds.Count >= 5, $"Plano deveria ter ao menos 5 passos, veio {kinds.Count}.");
        foreach (var (anterior, seguinte) in new[]
                 {
                     (RepairActionKind.ClearQueue, RepairActionKind.RestartSpooler),
                     (RepairActionKind.RestartSpooler, RepairActionKind.RestorePort),
                     (RepairActionKind.RestorePort, RepairActionKind.ReinstallWithIppClassDriver),
                     (RepairActionKind.ReinstallWithIppClassDriver, RepairActionKind.ReinstallFromDriverStore),
                     (RepairActionKind.ReinstallFromDriverStore, RepairActionKind.RemoveBrokenInstall),
                 })
        {
            Assert.True(kinds.IndexOf(anterior) < kinds.IndexOf(seguinte), $"{anterior} deve vir antes de {seguinte}.");
        }
    }

    [Fact]
    public void DestructiveSomenteNosPassosDeReinstallERemocao()
    {
        var report = CriarReport(fila: CheckResult.Warn, driver: CheckResult.Fail);
        var snapshot = CriarSnapshot();

        var plano = _planner.PlanRepairs(report, snapshot);

        foreach (var passo in plano.Steps)
        {
            if (passo.Kind is RepairActionKind.RemoveBrokenInstall
                or RepairActionKind.ReinstallWithIppClassDriver
                or RepairActionKind.ReinstallFromDriverStore)
            {
                Assert.True(passo.Destructive, $"{passo.Kind} deve ser destrutivo.");
            }
            else
            {
                Assert.False(passo.Destructive, $"{passo.Kind} não deve ser destrutivo.");
            }
        }
    }

    [Fact]
    public void ElevacaoObrigatoriaNosPassosContratuais()
    {
        var report = CriarReport(fila: CheckResult.Warn, porta: CheckResult.Fail, driver: CheckResult.Fail);
        var snapshot = CriarSnapshot(porta: new PortConfig("IP_192.168.0.40", "192.168.0.40", 9100, PrinterProtocol.TcpRaw));

        var plano = _planner.PlanRepairs(report, snapshot);

        foreach (var passo in plano.Steps)
        {
            if (passo.Kind is RepairActionKind.RestartSpooler
                or RepairActionKind.RestorePort
                or RepairActionKind.RemoveBrokenInstall
                or RepairActionKind.ReinstallWithIppClassDriver
                or RepairActionKind.ReinstallFromDriverStore)
            {
                Assert.True(passo.RequiresElevation, $"{passo.Kind} exige elevação.");
            }
        }

        Assert.True(plano.RequiresElevation);
    }

    private static int Indice(RepairPlan plano, RepairActionKind kind)
    {
        var indice = plano.Steps.ToList().FindIndex(s => s.Kind == kind);
        Assert.True(indice >= 0, $"Plano não contém {kind}.");
        return indice;
    }

    private static DiagnosticReport CriarReport(
        CheckResult fila = CheckResult.Pass,
        CheckResult porta = CheckResult.Pass,
        CheckResult driver = CheckResult.Pass)
    {
        var agora = DateTime.UtcNow;
        var checks = new List<CheckOutcome>
        {
            new(CheckId.SpoolerRunning, CheckResult.Pass, Severity.Info, "Spooler acessível."),
            new(CheckId.PortOpen, porta, porta == CheckResult.Pass ? Severity.Info : Severity.Error, $"Porta: {porta}."),
            new(CheckId.DriverPresent, driver, driver == CheckResult.Pass ? Severity.Info : Severity.Error, $"Driver: {driver}."),
            new(CheckId.QueueExists, CheckResult.Pass, Severity.Info, "Fila existe."),
            new(CheckId.QueueNotStuck, fila, fila == CheckResult.Pass ? Severity.Info : fila == CheckResult.Warn ? Severity.Warning : Severity.Error, $"Fila: {fila}."),
            new(CheckId.NoDuplicateInstall, CheckResult.Pass, Severity.Info, "Sem duplicidade."),
        };

        return new DiagnosticReport(Guid.NewGuid(), agora, agora.AddSeconds(2), checks, new RepairPlan(Guid.NewGuid(), [], false));
    }

    private static PrinterSnapshot CriarSnapshot(
        PortConfig? porta = null,
        DriverInfo? driver = null)
    {
        var alvo = TestTargets.Tcp();
        return new PrinterSnapshot(
            Id: Guid.NewGuid(),
            CreatedAtUtc: DateTime.UtcNow,
            Origin: SnapshotOrigin.PreRepair,
            Target: alvo,
            Port: porta,
            Queue: new QueueState(alvo.Name, Exists: true, StuckJobs: 0, DefaultPaperSize: "A4", CopiesDefault: 1, ColorDefault: false, DuplexDefault: true),
            Driver: driver,
            Permissions: new Dictionary<string, string>(),
            Defaults: new Dictionary<string, string>(),
            SchemaVersion: "1.0");
    }
}
