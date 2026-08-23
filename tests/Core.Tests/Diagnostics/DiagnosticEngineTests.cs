using PrinterRescue.Core.Diagnostics;
using PrinterRescue.Core.Interfaces;
using Xunit;

namespace PrinterRescue.Core.Tests.Diagnostics;

public sealed class DiagnosticEngineTests
{
    private static readonly PrinterTarget Alvo = TestTargets.Tcp();

    [Fact]
    public async Task ExecutaChecksNaOrdemDoEnumMesmoComListaForaDeOrdem()
    {
        var checks = new IDiagnosticCheck[]
        {
            new StubCheck(CheckId.NoDuplicateInstall, Passa(CheckId.NoDuplicateInstall)),
            new StubCheck(CheckId.QueueNotStuck, Passa(CheckId.QueueNotStuck)),
            new StubCheck(CheckId.SpoolerRunning, Passa(CheckId.SpoolerRunning)),
            new StubCheck(CheckId.DriverPresent, Passa(CheckId.DriverPresent)),
            new StubCheck(CheckId.PortOpen, Passa(CheckId.PortOpen)),
            new StubCheck(CheckId.QueueExists, Passa(CheckId.QueueExists)),
        };

        var report = await NovoEngine(checks).DiagnoseAndPlanAsync(Alvo);

        Assert.Equal(
            new[]
            {
                CheckId.SpoolerRunning,
                CheckId.PortOpen,
                CheckId.DriverPresent,
                CheckId.QueueExists,
                CheckId.QueueNotStuck,
                CheckId.NoDuplicateInstall,
            },
            report.Checks.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task ComSpoolerFalhoOsDemaisChecksFicamNotApplicableComGating()
    {
        var checks = new IDiagnosticCheck[]
        {
            new StubCheck(CheckId.SpoolerRunning, Falha(CheckId.SpoolerRunning)),
            new StubCheck(CheckId.PortOpen, Passa(CheckId.PortOpen)),
            new StubCheck(CheckId.DriverPresent, Passa(CheckId.DriverPresent)),
            new StubCheck(CheckId.QueueExists, Passa(CheckId.QueueExists)),
            new StubCheck(CheckId.QueueNotStuck, Passa(CheckId.QueueNotStuck)),
            new StubCheck(CheckId.NoDuplicateInstall, Passa(CheckId.NoDuplicateInstall)),
        };

        var report = await NovoEngine(checks).DiagnoseAndPlanAsync(Alvo);

        Assert.Equal(CheckResult.Fail, report.Checks[0].Result);
        foreach (var demais in report.Checks.Skip(1))
        {
            Assert.Equal(CheckResult.NotApplicable, demais.Result);
            Assert.Equal(Severity.Info, demais.Severity);
            Assert.Contains("spooler", demais.Detail, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ExcecaoEmCheckViraFailSemEstourar()
    {
        var checks = new IDiagnosticCheck[]
        {
            new StubCheck(CheckId.SpoolerRunning, Passa(CheckId.SpoolerRunning)),
            new ThrowingCheck(CheckId.DriverPresent),
            new StubCheck(CheckId.QueueExists, Passa(CheckId.QueueExists)),
        };

        var report = await NovoEngine(checks).DiagnoseAndPlanAsync(Alvo);

        var driver = Assert.Single(report.Checks, c => c.Id == CheckId.DriverPresent);
        Assert.Equal(CheckResult.Fail, driver.Result);
        Assert.Equal(Severity.Error, driver.Severity);
        Assert.Contains("boom", driver.Detail, StringComparison.OrdinalIgnoreCase);
        // A exceção não interrompe o diagnóstico: o check seguinte ainda executa.
        Assert.Single(report.Checks, c => c.Id == CheckId.QueueExists);
    }

    [Fact]
    public async Task UsaSnapshotStoreParaResolverUltimoBom()
    {
        var snapshot = CriarSnapshot();
        var store = new RecordingSnapshotStore(snapshot);
        var planner = new FakeRepairPlanner();

        _ = await NovoEngine(TodosPassando(), planner, store).DiagnoseAndPlanAsync(Alvo);

        Assert.Equal(TestTargets.NomePadrao, store.NomeConsultado);
        Assert.Equal(snapshot, planner.LastGood);
    }

    [Fact]
    public async Task SemSnapshotStorePassaLastGoodNuloAoPlanner()
    {
        var planner = new FakeRepairPlanner();

        _ = await NovoEngine(TodosPassando(), planner, store: null).DiagnoseAndPlanAsync(Alvo);

        Assert.Null(planner.LastGood);
        Assert.Equal(1, planner.Calls);
    }

    [Fact]
    public async Task RelatorioTemTimestampsReaisEPlanoAnexado()
    {
        var antes = DateTime.UtcNow;

        var report = await NovoEngine(TodosPassando()).DiagnoseAndPlanAsync(Alvo);

        var depois = DateTime.UtcNow;
        Assert.NotEqual(Guid.Empty, report.TargetId);
        Assert.InRange(report.StartedAtUtc, antes, depois);
        Assert.InRange(report.FinishedAtUtc, report.StartedAtUtc, depois);
        Assert.Equal(6, report.Checks.Count);
    }

    [Fact]
    public async Task ConstrutorComArgumentoNuloLanca()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Task.FromResult(new DiagnosticEngine(null!, [], new FakeRepairPlanner())));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Task.FromResult(new DiagnosticEngine(new FakePrintGateway(), null!, new FakeRepairPlanner())));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Task.FromResult(new DiagnosticEngine(new FakePrintGateway(), [], null!)));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new DiagnosticEngine(new FakePrintGateway(), TodosPassando(), new FakeRepairPlanner())
                .DiagnoseAndPlanAsync(null!));
    }

    private static DiagnosticEngine NovoEngine(
        IReadOnlyList<IDiagnosticCheck> checks,
        IRepairPlanner? planner = null,
        ISnapshotStore? store = null)
        => new(new FakePrintGateway(), checks, planner ?? new FakeRepairPlanner(), store);

    private static IReadOnlyList<IDiagnosticCheck> TodosPassando() =>
    [
        new StubCheck(CheckId.SpoolerRunning, Passa(CheckId.SpoolerRunning)),
        new StubCheck(CheckId.PortOpen, Passa(CheckId.PortOpen)),
        new StubCheck(CheckId.DriverPresent, Passa(CheckId.DriverPresent)),
        new StubCheck(CheckId.QueueExists, Passa(CheckId.QueueExists)),
        new StubCheck(CheckId.QueueNotStuck, Passa(CheckId.QueueNotStuck)),
        new StubCheck(CheckId.NoDuplicateInstall, Passa(CheckId.NoDuplicateInstall)),
    ];

    private static CheckOutcome Passa(CheckId id) => new(id, CheckResult.Pass, Severity.Info, "OK (stub).");

    private static CheckOutcome Falha(CheckId id) => new(id, CheckResult.Fail, Severity.Error, "Falhou (stub).");

    private static PrinterSnapshot CriarSnapshot() => new(
        Guid.NewGuid(),
        DateTime.UtcNow.AddDays(-1),
        SnapshotOrigin.Manual,
        Alvo,
        Port: null,
        Queue: null,
        Driver: null,
        Permissions: new Dictionary<string, string>(),
        Defaults: new Dictionary<string, string>(),
        SchemaVersion: "1");
}
