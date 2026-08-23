using PrinterRescue.Core.Snapshots;
using Xunit;

namespace PrinterRescue.Core.Tests.Snapshots;

/// <summary>Fixtures válidas para os testes de persistência.</summary>
public static class SnapshotFixtures
{
    public const string SchemaVersion = "1.0";

    public static PrinterTarget Alvo(string nome = TestTargets.NomePadrao) =>
        new(nome, ShareName: null, PortName: "IP_192.168.0.40", PrinterProtocol.TcpRaw,
            DeviceId: null, DriverName: "HP Universal PCL6", DriverVersion: "3.12.0.0");

    public static PortConfig Porta() =>
        new("IP_192.168.0.40", "192.168.0.40", 9100, PrinterProtocol.TcpRaw);

    public static QueueState Fila() =>
        new(TestTargets.NomePadrao, Exists: true, StuckJobs: 0, DefaultPaperSize: "A4",
            CopiesDefault: 1, ColorDefault: false, DuplexDefault: true);

    public static DriverInfo Driver(bool ipp = false) =>
        new("HP Universal PCL6", "3.12.0.0", InfName: "hpcu270u.inf",
            PresentInDriverStore: true, IsIppClassDriver: ipp);

    public static PrinterSnapshot Snapshot(
        string nome = TestTargets.NomePadrao,
        DateTime? criadoEm = null,
        Guid? id = null,
        SnapshotOrigin origem = SnapshotOrigin.Manual,
        bool driverIpp = false) =>
        new(
            Id: id ?? Guid.NewGuid(),
            CreatedAtUtc: criadoEm ?? new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc),
            Origin: origem,
            Target: Alvo(nome),
            Port: Porta(),
            Queue: Fila(),
            Driver: Driver(driverIpp),
            Permissions: new Dictionary<string, string> { ["queueSddl"] = "G:SYD:PAI(A;;FA;;;BA)" },
            Defaults: new Dictionary<string, string> { ["paperSize"] = "A4", ["copies"] = "1" },
            SchemaVersion: SchemaVersion);
}

public sealed class SnapshotStoreTests : IDisposable
{
    private readonly string _raiz;

    public SnapshotStoreTests()
    {
        _raiz = Path.Combine(Path.GetTempPath(), "pr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_raiz);
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz))
        {
            Directory.Delete(_raiz, recursive: true);
        }
    }

    [Fact]
    public async Task SalvarGravaJsonERecuperaRegistroIgual()
    {
        var loja = new SnapshotStore(_raiz);
        var original = SnapshotFixtures.Snapshot(origem: SnapshotOrigin.PreRepair);

        await loja.SaveAsync(original);
        var carregado = await loja.LoadAsync(original.Id);

        Assert.NotNull(carregado);
        // Comparação campo a campo: records com membros Dictionary usam igualdade por
        // referência nos dicionários, então Assert.Equal de objeto nunca passa após
        // round-trip JSON mesmo com conteúdo idêntico.
        Assert.Equal(original.Id, carregado.Id);
        Assert.Equal(original.CreatedAtUtc, carregado.CreatedAtUtc);
        Assert.Equal(original.Origin, carregado.Origin);
        Assert.Equal(original.Target, carregado.Target);
        Assert.Equal(original.Port, carregado.Port);
        Assert.Equal(original.Queue, carregado.Queue);
        Assert.Equal(original.Driver, carregado.Driver);
        Assert.Equal(original.SchemaVersion, carregado.SchemaVersion);
        Assert.True(original.Permissions.Count == carregado.Permissions.Count
            && original.Permissions.All(kv => carregado.Permissions[kv.Key] == kv.Value));
        Assert.True(original.Defaults.Count == carregado.Defaults.Count
            && original.Defaults.All(kv => carregado.Defaults[kv.Key] == kv.Value));
        Assert.True(File.Exists(Path.Combine(_raiz, original.Id + ".json")));
    }

    [Fact]
    public async Task ArquivoGravadoContemSchemaVersionECamelCase()
    {
        var loja = new SnapshotStore(_raiz);
        // origem PreRepair para exercitar a forma kebab-case ("pre-repair") na escrita
        var snap = SnapshotFixtures.Snapshot(origem: SnapshotOrigin.PreRepair);

        await loja.SaveAsync(snap);

        var texto = await File.ReadAllTextAsync(Path.Combine(_raiz, snap.Id + ".json"));
        Assert.Contains("\"schemaVersion\": \"1.0\"", texto);
        Assert.Contains("\"createdAtUtc\"", texto);
        Assert.Contains("\"tcp_raw\"", texto); // protocolo em snake_case
        Assert.Contains("\"pre-repair\"", texto); // origem em kebab-case quando PreRepair
    }

    [Fact]
    public async Task CarregarIdInexistenteRetornaNull()
    {
        var loja = new SnapshotStore(_raiz);

        var resultado = await loja.LoadAsync(Guid.NewGuid());

        Assert.Null(resultado);
    }

    [Fact]
    public async Task FindLatestForRetornaMaisRecentePorNome()
    {
        var loja = new SnapshotStore(_raiz);
        var antigo = SnapshotFixtures.Snapshot(criadoEm: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        var novo = SnapshotFixtures.Snapshot(id: antigo.Id, criadoEm: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
        // ids distintos para dois arquivos coexistirem
        var antigoComOutroId = antigo with { Id = Guid.NewGuid() };

        await loja.SaveAsync(antigoComOutroId);
        await loja.SaveAsync(novo);

        var encontrado = await loja.FindLatestForAsync(TestTargets.NomePadrao);

        Assert.NotNull(encontrado);
        Assert.Equal(novo.Id, encontrado.Id);
    }

    [Fact]
    public async Task FindLatestForSemSnapshotsRetornaNull()
    {
        var loja = new SnapshotStore(_raiz);

        var encontrado = await loja.FindLatestForAsync("Impressora Fantasma");

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task ListAsyncRetornaResumosDeTodosOsArquivos()
    {
        var loja = new SnapshotStore(_raiz);
        await loja.SaveAsync(SnapshotFixtures.Snapshot(nome: "A"));
        await loja.SaveAsync(SnapshotFixtures.Snapshot(nome: "B"));

        var lista = await loja.ListAsync();

        Assert.Equal(2, lista.Count);
        Assert.All(lista, s => Assert.False(s.Id == default));
        Assert.Contains(lista, s => s.PrinterName == "A");
        Assert.Contains(lista, s => s.PrinterName == "B");
    }

    [Fact]
    public async Task DeleteAllLimpaORaiz()
    {
        var loja = new SnapshotStore(_raiz);
        await loja.SaveAsync(SnapshotFixtures.Snapshot());

        await loja.DeleteAllAsync();

        Assert.Empty(Directory.GetFiles(_raiz));
        Assert.Empty(await loja.ListAsync());
    }

    [Fact]
    public async Task CarregarArquivoAcimaDeUmMebibyteRejeita()
    {
        var loja = new SnapshotStore(_raiz);
        var id = Guid.NewGuid();
        var caminho = Path.Combine(_raiz, id + ".json");
        await File.WriteAllBytesAsync(caminho, new byte[1024 * 1024 + 1]);

        await Assert.ThrowsAnyAsync<InvalidDataException>(() => loja.LoadAsync(id));
    }

    [Fact]
    public async Task CarregarJsonProfundoDemaisRejeita()
    {
        var loja = new SnapshotStore(_raiz);
        var id = Guid.NewGuid();
        var caminho = Path.Combine(_raiz, id + ".json");
        // profundidade 20 > limite 16
        var profundo = new string('[', 20) + new string(']', 20);
        await File.WriteAllTextAsync(caminho, profundo);

        await Assert.ThrowsAnyAsync<InvalidDataException>(() => loja.LoadAsync(id));
    }

    [Fact]
    public async Task CarregarJsonSemCamposObrigatoriosRejeita()
    {
        var loja = new SnapshotStore(_raiz);
        var id = Guid.NewGuid();
        var caminho = Path.Combine(_raiz, id + ".json");
        await File.WriteAllTextAsync(caminho, "{ \"nota\": \"sem schema nem target\" }");

        await Assert.ThrowsAnyAsync<InvalidDataException>(() => loja.LoadAsync(id));
    }

    [Fact]
    public async Task NomeComTravessiaNuncaEscapaDaRaiz()
    {
        var loja = new SnapshotStore(_raiz);
        var malicioso = "../../../Windows";

        // não deve lançar por traversal nem gravar fora da raiz
        var snap = SnapshotFixtures.Snapshot(nome: malicioso);
        await loja.SaveAsync(snap);

        Assert.NotEmpty(Directory.GetFiles(_raiz));
        var encontrado = await loja.FindLatestForAsync(malicioso);
        Assert.NotNull(encontrado);
        Assert.Equal(malicioso, encontrado.Target.Name);
        // nada foi criado fora da raiz temporária
        Assert.Single(Directory.GetFiles(_raiz));
    }
}
