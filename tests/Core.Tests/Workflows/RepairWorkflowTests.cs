using PrinterRescue.Core.Interfaces;
using PrinterRescue.Core.Snapshots;
using PrinterRescue.Core.Workflows;
using PrinterRescue.Core.Tests.Snapshots;
using Xunit;

namespace PrinterRescue.Core.Tests.Workflows;

/// <summary>Executor falso que registra os passos recebidos e devolve resultado configurável.</summary>
public sealed class FakeExecutor : IRepairExecutor
{
    public List<RepairStep> Recebidos { get; } = [];

    /// <summary>Status devolvido para cada passo, na ordem. Último valor repete.</summary>
    public Queue<RepairStatus> Statuses { get; init; } = new();

    public bool Elevado { get; set; } = true;

    public bool IsElevated() => Elevado;

    public Task<RepairOutcome> ExecuteAsync(RepairStep action, PrinterSnapshot context, CancellationToken ct = default)
    {
        Recebidos.Add(action);
        var status = Statuses.Count > 0 ? Statuses.Dequeue() : RepairStatus.Applied;
        return Task.FromResult(new RepairOutcome(Guid.NewGuid(), context.Id, action.Kind, status, "executado"));
    }
}

/// <summary>Loja falsa que registra snapshots salvos.</summary>
public sealed class FakeStore : ISnapshotStore
{
    public List<PrinterSnapshot> Salvos { get; } = [];

    public Task SaveAsync(PrinterSnapshot snapshot, CancellationToken ct = default)
    {
        Salvos.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task<PrinterSnapshot?> FindLatestForAsync(string printerName, CancellationToken ct = default)
        => Task.FromResult<PrinterSnapshot?>(null);

    public Task<IReadOnlyList<SnapshotSummary>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SnapshotSummary>>([]);

    public Task<PrinterSnapshot?> LoadAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<PrinterSnapshot?>(Salvos.FirstOrDefault(s => s.Id == id));

    public Task DeleteAllAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Guarda falsa: nega passos cujo Kind esteja na lista.</summary>
public sealed class FakeGuard(params RepairActionKind[] negar) : IPolicyGuard
{
    public List<RepairActionKind> Negados { get; } = [.. negar];

    public PolicyDecision Evaluate(RepairStep action, PrinterSnapshot context)
        => Negados.Contains(action.Kind)
            ? new PolicyDecision(false, $"Passo {action.Kind} viola a REGRA Nº 1.")
            : new PolicyDecision(true, "OK");
}

public static class WorkflowFixtures
{
    public static RepairStep Passo(RepairActionKind kind, bool destrutivo = false, bool elevacao = false) =>
        new(kind, Description: kind.ToString(), Destructive: destrutivo, RequiresElevation: elevacao);

    public static RepairPlan Plano(params RepairStep[] passos) =>
        new(Guid.NewGuid(), passos, passos.Any(p => p.RequiresElevation));
}

public sealed class RepairWorkflowTests
{
    [Fact]
    public async Task FluxoFelizExecutaTodosOsPassosNaOrdem()
    {
        var executor = new FakeExecutor();
        var wf = new RepairWorkflow(executor, new FakeGuard(), store: null, capture: new CaptureWorkflow(new FakePrintGateway()));
        var snap = SnapshotFixtures.Snapshot();
        var plano = WorkflowFixtures.Plano(
            WorkflowFixtures.Passo(RepairActionKind.ClearQueue),
            WorkflowFixtures.Passo(RepairActionKind.RestartSpooler, elevacao: true));

        var resultados = await wf.RunAsync(plano, snap);

        Assert.All(resultados, r => Assert.Equal(RepairStatus.Applied, r.Status));
        Assert.Equal(2, executor.Recebidos.Count);
        Assert.Equal(RepairActionKind.ClearQueue, executor.Recebidos[0].Kind);
        Assert.Equal(RepairActionKind.RestartSpooler, executor.Recebidos[1].Kind);
    }

    [Fact]
    public async Task PassoNegadoPelaPoliticaViraSkippedPolicyViolationEContinua()
    {
        var executor = new FakeExecutor();
        var wf = new RepairWorkflow(executor, new FakeGuard(RepairActionKind.ReinstallFromDriverStore),
            store: null, capture: new CaptureWorkflow(new FakePrintGateway()));
        var snap = SnapshotFixtures.Snapshot();
        var plano = WorkflowFixtures.Plano(
            WorkflowFixtures.Passo(RepairActionKind.ReinstallFromDriverStore),
            WorkflowFixtures.Passo(RepairActionKind.RestoreDefaults));

        var resultados = await wf.RunAsync(plano, snap);

        Assert.Equal(RepairStatus.SkippedPolicyViolation, resultados[0].Status);
        Assert.Contains("REGRA Nº 1", resultados[0].Detail);
        Assert.Equal(RepairStatus.Applied, resultados[1].Status); // seguiu o plano
    }

    [Fact]
    public async Task DestrutivoSemSnapshotViraSkippedNoSnapshotENaoExecuta()
    {
        var executor = new FakeExecutor();
        var wf = new RepairWorkflow(executor, new FakeGuard(), store: null, capture: new CaptureWorkflow(new FakePrintGateway()));
        var plano = WorkflowFixtures.Plano(
            WorkflowFixtures.Passo(RepairActionKind.RemoveBrokenInstall, destrutivo: true),
            WorkflowFixtures.Passo(RepairActionKind.ClearQueue));

        var resultados = await wf.RunAsync(plano, lastGood: null);

        Assert.Equal(RepairStatus.SkippedNoSnapshot, resultados[0].Status);
        Assert.DoesNotContain(executor.Recebidos, s => s.Kind == RepairActionKind.RemoveBrokenInstall);
        Assert.Equal(RepairStatus.Applied, resultados[1].Status);
    }

    [Fact]
    public async Task DestrutivoSemElevacaoFalhaAntesDeExecutar()
    {
        var executor = new FakeExecutor { Elevado = false };
        var wf = new RepairWorkflow(executor, new FakeGuard(), store: null, capture: new CaptureWorkflow(new FakePrintGateway()));
        var snap = SnapshotFixtures.Snapshot();
        var plano = WorkflowFixtures.Plano(
            WorkflowFixtures.Passo(RepairActionKind.RemoveBrokenInstall, destrutivo: true, elevacao: true));

        var resultados = await wf.RunAsync(plano, snap);

        Assert.Equal(RepairStatus.Failed, resultados[0].Status);
        Assert.Empty(executor.Recebidos);
    }

    [Fact]
    public async Task PrimeiroDestrutivoGravaSnapshotPreRepairUmaUnicaVez()
    {
        var executor = new FakeExecutor();
        var loja = new FakeStore();
        var wf = new RepairWorkflow(executor, new FakeGuard(), loja, capture: new CaptureWorkflow(new FakePrintGateway()));
        var snap = SnapshotFixtures.Snapshot();
        var plano = WorkflowFixtures.Plano(
            WorkflowFixtures.Passo(RepairActionKind.ClearQueue),
            WorkflowFixtures.Passo(RepairActionKind.RemoveBrokenInstall, destrutivo: true),
            WorkflowFixtures.Passo(RepairActionKind.ReinstallFromDriverStore, destrutivo: true));

        await wf.RunAsync(plano, snap);

        var pre = loja.Salvos.Where(s => s.Origin == SnapshotOrigin.PreRepair).ToList();
        _ = Assert.Single(pre);
    }

    [Fact]
    public async Task HouveAplicadoGravaSnapshotPostRepair()
    {
        var executor = new FakeExecutor();
        var loja = new FakeStore();
        var wf = new RepairWorkflow(executor, new FakeGuard(), loja, capture: new CaptureWorkflow(new FakePrintGateway()));
        var snap = SnapshotFixtures.Snapshot();
        var plano = WorkflowFixtures.Plano(WorkflowFixtures.Passo(RepairActionKind.ClearQueue));

        await wf.RunAsync(plano, snap);

        var post = loja.Salvos.Where(s => s.Origin == SnapshotOrigin.PostRepair).ToList();
        _ = Assert.Single(post);
    }

    [Fact]
    public async Task NadaAplicadoNaoGravaPostRepair()
    {
        var executor = new FakeExecutor { Statuses = new Queue<RepairStatus>([RepairStatus.Failed]) };
        var loja = new FakeStore();
        var wf = new RepairWorkflow(executor, new FakeGuard(), loja, capture: new CaptureWorkflow(new FakePrintGateway()));
        var plano = WorkflowFixtures.Plano(WorkflowFixtures.Passo(RepairActionKind.ClearQueue));

        await wf.RunAsync(plano, SnapshotFixtures.Snapshot());

        Assert.DoesNotContain(loja.Salvos, s => s.Origin == SnapshotOrigin.PostRepair);
    }

    [Fact]
    public async Task SemSnapshotEDestrutivosSomentePlanoTerminaVazioDeExecucaoReal()
    {
        var executor = new FakeExecutor();
        var wf = new RepairWorkflow(executor, new FakeGuard(), store: null, capture: new CaptureWorkflow(new FakePrintGateway()));
        var plano = WorkflowFixtures.Plano(
            WorkflowFixtures.Passo(RepairActionKind.RemoveBrokenInstall, destrutivo: true),
            WorkflowFixtures.Passo(RepairActionKind.ReinstallWithIppClassDriver, destrutivo: true));

        var resultados = await wf.RunAsync(plano, lastGood: null);

        Assert.All(resultados, r => Assert.Equal(RepairStatus.SkippedNoSnapshot, r.Status));
        Assert.Empty(executor.Recebidos);
    }
}

public sealed class CaptureWorkflowTests
{
    [Fact]
    public async Task CapturaMontaSnapshotCompletoDoGateway()
    {
        var alvo = TestTargets.Tcp();
        var gateway = new FakePrintGateway(
            printers: [alvo],
            port: SnapshotFixtures.Porta(),
            queue: SnapshotFixtures.Fila(),
            driver: SnapshotFixtures.Driver());
        var wf = new CaptureWorkflow(gateway);

        var snap = await wf.CaptureAsync(alvo, SnapshotOrigin.Manual);

        Assert.Equal(SnapshotStore.SchemaVersionAtual, snap.SchemaVersion);
        Assert.Equal(SnapshotOrigin.Manual, snap.Origin);
        Assert.NotNull(snap.Port);
        Assert.NotNull(snap.Queue);
        Assert.NotNull(snap.Driver);
        Assert.False(snap.Id == default);
    }

    [Fact]
    public async Task CapturaSemPortaOuDriverNomeadosDeixaCamposNulos()
    {
        var alvo = TestTargets.Tcp(portName: null) with { DriverName = null };
        var gateway = new FakePrintGateway(queue: SnapshotFixtures.Fila());
        var wf = new CaptureWorkflow(gateway);

        var snap = await wf.CaptureAsync(alvo, SnapshotOrigin.PreRepair);

        Assert.Null(snap.Port);
        Assert.Null(snap.Driver);
        Assert.NotNull(snap.Queue);
        Assert.Equal(SnapshotOrigin.PreRepair, snap.Origin);
    }
}
