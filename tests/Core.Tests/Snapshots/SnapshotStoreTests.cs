using PrinterRescue.Core.Snapshots;
using Xunit;

namespace PrinterRescue.Core.Tests.Snapshots;

/// <summary>Valid fixtures for persistence tests.</summary>
public static class SnapshotFixtures
{
    public const string SchemaVersion = "1.0";

    public static PrinterTarget Target(string name = TestTargets.DefaultName) =>
        new(name, ShareName: null, PortName: "IP_192.168.0.40", PrinterProtocol.TcpRaw,
            DeviceId: null, DriverName: "HP Universal PCL6", DriverVersion: "3.12.0.0");

    public static PortConfig Port() =>
        new("IP_192.168.0.40", "192.168.0.40", 9100, PrinterProtocol.TcpRaw);

    public static QueueState Queue() =>
        new(TestTargets.DefaultName, Exists: true, StuckJobs: 0, DefaultPaperSize: "A4",
            CopiesDefault: 1, ColorDefault: false, DuplexDefault: true);

    public static DriverInfo Driver(bool ipp = false) =>
        new("HP Universal PCL6", "3.12.0.0", InfName: "hpcu270u.inf",
            PresentInDriverStore: true, IsIppClassDriver: ipp);

    public static PrinterSnapshot Snapshot(
        string name = TestTargets.DefaultName,
        DateTime? createdAt = null,
        Guid? id = null,
        SnapshotOrigin origin = SnapshotOrigin.Manual,
        bool ippDriver = false) =>
        new(
            Id: id ?? Guid.NewGuid(),
            CreatedAtUtc: createdAt ?? new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc),
            Origin: origin,
            Target: Target(name),
            Port: Port(),
            Queue: Queue(),
            Driver: Driver(ippDriver),
            Permissions: new Dictionary<string, string> { ["queueSddl"] = "G:SYD:PAI(A;;FA;;;BA)" },
            Defaults: new Dictionary<string, string> { ["paperSize"] = "A4", ["copies"] = "1" },
            SchemaVersion: SchemaVersion);
}

public sealed class SnapshotStoreTests : IDisposable
{
    private readonly string _root;

    public SnapshotStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveWritesJsonAndReadsBackEqualRecord()
    {
        var store = new SnapshotStore(_root);
        var original = SnapshotFixtures.Snapshot(origin: SnapshotOrigin.PreRepair);

        await store.SaveAsync(original);
        var loaded = await store.LoadAsync(original.Id);

        Assert.NotNull(loaded);
        // Field-by-field comparison: records with Dictionary members use reference
        // equality on dictionaries, so object Assert.Equal never passes after a
        // JSON round-trip even with identical content.
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(original.Origin, loaded.Origin);
        Assert.Equal(original.Target, loaded.Target);
        Assert.Equal(original.Port, loaded.Port);
        Assert.Equal(original.Queue, loaded.Queue);
        Assert.Equal(original.Driver, loaded.Driver);
        Assert.Equal(original.SchemaVersion, loaded.SchemaVersion);
        Assert.True(original.Permissions.Count == loaded.Permissions.Count
            && original.Permissions.All(kv => loaded.Permissions[kv.Key] == kv.Value));
        Assert.True(original.Defaults.Count == loaded.Defaults.Count
            && original.Defaults.All(kv => loaded.Defaults[kv.Key] == kv.Value));
        Assert.True(File.Exists(Path.Combine(_root, original.Id + ".json")));
    }

    [Fact]
    public async Task WrittenFileContainsSchemaVersionAndCamelCase()
    {
        var store = new SnapshotStore(_root);
        // PreRepair origin to exercise the kebab-case form ("pre-repair") on write
        var snap = SnapshotFixtures.Snapshot(origin: SnapshotOrigin.PreRepair);

        await store.SaveAsync(snap);

        var text = await File.ReadAllTextAsync(Path.Combine(_root, snap.Id + ".json"));
        Assert.Contains("\"schemaVersion\": \"1.0\"", text);
        Assert.Contains("\"createdAtUtc\"", text);
        Assert.Contains("\"tcp_raw\"", text); // protocol in snake_case
        Assert.Contains("\"pre-repair\"", text); // origin in kebab-case for PreRepair
    }

    [Fact]
    public async Task LoadMissingIdReturnsNull()
    {
        var store = new SnapshotStore(_root);

        var result = await store.LoadAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task FindLatestForReturnsNewestByName()
    {
        var store = new SnapshotStore(_root);
        var oldSnapshot = SnapshotFixtures.Snapshot(createdAt: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        var newSnapshot = SnapshotFixtures.Snapshot(id: oldSnapshot.Id, createdAt: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc));
        // distinct ids so two files can coexist
        var oldWithOtherId = oldSnapshot with { Id = Guid.NewGuid() };

        await store.SaveAsync(oldWithOtherId);
        await store.SaveAsync(newSnapshot);

        var found = await store.FindLatestForAsync(TestTargets.DefaultName);

        Assert.NotNull(found);
        Assert.Equal(newSnapshot.Id, found.Id);
    }

    [Fact]
    public async Task FindLatestForWithoutSnapshotsReturnsNull()
    {
        var store = new SnapshotStore(_root);

        var found = await store.FindLatestForAsync("Ghost Printer");

        Assert.Null(found);
    }

    [Fact]
    public async Task ListAsyncReturnsSummariesOfAllFiles()
    {
        var store = new SnapshotStore(_root);
        await store.SaveAsync(SnapshotFixtures.Snapshot(name: "A"));
        await store.SaveAsync(SnapshotFixtures.Snapshot(name: "B"));

        var list = await store.ListAsync();

        Assert.Equal(2, list.Count);
        Assert.All(list, s => Assert.False(s.Id == default));
        Assert.Contains(list, s => s.PrinterName == "A");
        Assert.Contains(list, s => s.PrinterName == "B");
    }

    [Fact]
    public async Task DeleteAllClearsRoot()
    {
        var store = new SnapshotStore(_root);
        await store.SaveAsync(SnapshotFixtures.Snapshot());

        await store.DeleteAllAsync();

        Assert.Empty(Directory.GetFiles(_root));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task LoadFileOverOneMebibyteRejects()
    {
        var store = new SnapshotStore(_root);
        var id = Guid.NewGuid();
        var path = Path.Combine(_root, id + ".json");
        await File.WriteAllBytesAsync(path, new byte[1024 * 1024 + 1]);

        await Assert.ThrowsAnyAsync<InvalidDataException>(() => store.LoadAsync(id));
    }

    [Fact]
    public async Task LoadTooDeepJsonRejects()
    {
        var store = new SnapshotStore(_root);
        var id = Guid.NewGuid();
        var path = Path.Combine(_root, id + ".json");
        // depth 20 > limit 16
        var deep = new string('[', 20) + new string(']', 20);
        await File.WriteAllTextAsync(path, deep);

        await Assert.ThrowsAnyAsync<InvalidDataException>(() => store.LoadAsync(id));
    }

    [Fact]
    public async Task LoadJsonWithoutRequiredFieldsRejects()
    {
        var store = new SnapshotStore(_root);
        var id = Guid.NewGuid();
        var path = Path.Combine(_root, id + ".json");
        await File.WriteAllTextAsync(path, "{ \"note\": \"no schema or target\" }");

        await Assert.ThrowsAnyAsync<InvalidDataException>(() => store.LoadAsync(id));
    }

    [Fact]
    public async Task TraversalNameNeverEscapesRoot()
    {
        var store = new SnapshotStore(_root);
        var malicious = "../../../Windows";

        // must not throw on traversal nor write outside the root
        var snap = SnapshotFixtures.Snapshot(name: malicious);
        await store.SaveAsync(snap);

        Assert.NotEmpty(Directory.GetFiles(_root));
        var found = await store.FindLatestForAsync(malicious);
        Assert.NotNull(found);
        Assert.Equal(malicious, found.Target.Name);
        // nothing was created outside the temp root
        Assert.Single(Directory.GetFiles(_root));
    }
}
