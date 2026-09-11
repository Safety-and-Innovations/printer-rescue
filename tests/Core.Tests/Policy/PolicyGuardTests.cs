using PrinterRescue.Core.Policy;
using Xunit;

namespace PrinterRescue.Core.Tests.Policy;

/// <summary>Compliance guard tests — RULE #1 (never download, host, or distribute drivers).</summary>
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
    public void EvaluateWithSnapshotAndDefaultsAreAllowed(RepairActionKind kind)
    {
        var step = new RepairStep(kind, "Perform local printer maintenance procedure.", Destructive: false, RequiresElevation: false);

        var decision = _guard.Evaluate(step, CreateSnapshot());

        Assert.True(decision.Allowed);
        Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
    }

    [Theory]
    [InlineData("Baixar driver do site do fabricante")]
    [InlineData("Fazer DOWNLOAD do pacote de driver completo")]
    [InlineData("Hospedar driver em servidor interno da rede")]
    [InlineData("Distribuir driver para as estacoes de trabalho")]
    [InlineData("Reinstalar usando catalogo de driver da web")]
    [InlineData("Instalar driver externo assinado pelo fabricante")]
    [InlineData("Rotina que vai Baixar e Hospedar driver no compartilhamento")]
    public void EvaluateDeniesDescriptionWithDriverDistributionTerm(string description)
    {
        var step = new RepairStep(RepairActionKind.ReinstallFromDriverStore, description, Destructive: true, RequiresElevation: true);

        var decision = _guard.Evaluate(step, CreateSnapshot());

        Assert.False(decision.Allowed);
        Assert.Contains("RULE", decision.Reason);
    }

    [Fact]
    public void EvaluateDeniesRegardlessOfKindWhenDescriptionIsVector()
    {
        // Unknown Kind values outside the enum do not exist in C#; the main violation vector is the Description.
        var step = new RepairStep(
            RepairActionKind.RestoreDefaults,
            "Publish driver: hospedar driver in an online repository and distribuir driver to branches.",
            Destructive: false,
            RequiresElevation: false);

        var decision = _guard.Evaluate(step, CreateSnapshot());

        Assert.False(decision.Allowed);
    }

    [Fact]
    public void EvaluateDenialReportsDetectedTerm()
    {
        var step = new RepairStep(RepairActionKind.ClearQueue, "Routine that will baixar driver before clearing the queue.", Destructive: false, RequiresElevation: false);

        var decision = _guard.Evaluate(step, CreateSnapshot());

        Assert.False(decision.Allowed);
        Assert.Contains("baixar", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateDoesNotDependOnSnapshotToDecide()
    {
        var safeStep = new RepairStep(RepairActionKind.ClearQueue, "Clear the print queue.", Destructive: false, RequiresElevation: false);
        var forbiddenStep = new RepairStep(RepairActionKind.ClearQueue, "Baixar driver before clearing the queue.", Destructive: false, RequiresElevation: false);

        var allowed = _guard.Evaluate(safeStep, CreateSnapshot());
        var denied = _guard.Evaluate(forbiddenStep, CreateSnapshot());

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
    }

    private static PrinterSnapshot CreateSnapshot()
    {
        var target = TestTargets.Tcp();
        return new PrinterSnapshot(
            Id: Guid.NewGuid(),
            CreatedAtUtc: DateTime.UtcNow,
            Origin: SnapshotOrigin.PreRepair,
            Target: target,
            Port: new PortConfig("IP_192.168.0.40", "192.168.0.40", 9100, PrinterProtocol.TcpRaw),
            Queue: new QueueState(target.Name, Exists: true, StuckJobs: 0, DefaultPaperSize: "A4", CopiesDefault: 1, ColorDefault: false, DuplexDefault: true),
            Driver: new DriverInfo("HP Universal PCL6", "3.12.0.0", InfName: null, PresentInDriverStore: true, IsIppClassDriver: false),
            Permissions: new Dictionary<string, string>(),
            Defaults: new Dictionary<string, string>(),
            SchemaVersion: "1.0");
    }
}
