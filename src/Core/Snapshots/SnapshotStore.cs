using System.Text.Json;
using System.Text.Json.Serialization;
using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Snapshots;

/// <summary>
/// Snapshot persistence to JSON files — one per snapshot, named by Id.
/// Hardened parser: 1 MiB max size, max depth 16, no traversal
/// (printer names never become paths).
/// </summary>
public sealed class SnapshotStore : ISnapshotStore
{
    public const string CurrentSchemaVersion = "1.0";
    private const long MaxSizeBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            // SnapshotOrigin in kebab-case ("pre-repair"); other enums in snake_case
            // ("tcp_raw"). The first compatible converter wins on write.
            new OriginKebabWriter(),
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
        },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 16,
        Converters =
        {
            // Single enum converter on read: accepts PascalCase, kebab-case
            // ("pre-repair"), snake_case ("pre_repair") and numeric — the set that
            // writing itself produces. Do not register JsonStringEnumConverter alongside:
            // the first in the list wins and would reject kebab/snake forms.
            new EnumKebabOrCamelConverter(),
        },
    };

    private readonly string _root;

    public SnapshotStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_root);
    }

    /// <summary>Physical snapshot root (for diagnostics and tests).</summary>
    public string RootDirectory => _root;

    public async Task SaveAsync(PrinterSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!string.Equals(snapshot.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"unknown schemaVersion '{snapshot.SchemaVersion}'; expected '{CurrentSchemaVersion}'.");
        }

        ValidateRequired(snapshot);

        var path = PathFor(snapshot.Id);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, WriteOptions), ct)
            .ConfigureAwait(false);
    }

    public async Task<PrinterSnapshot?> LoadAsync(Guid id, CancellationToken ct = default)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        if (info.Length > MaxSizeBytes)
        {
            throw new InvalidDataException($"Snapshot {id} exceeds the 1 MiB limit.");
        }

        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return Deserialize(text, id);
    }

    public Task<PrinterSnapshot?> FindLatestForAsync(string printerName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        var latest = ListInternal(ct)
            .Where(s => string.Equals(s.Target.Name, printerName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => (PrinterSnapshot?)s)
            .FirstOrDefault();

        return Task.FromResult(latest);
    }

    public Task<IReadOnlyList<SnapshotSummary>> ListAsync(CancellationToken ct = default)
    {
        IReadOnlyList<SnapshotSummary> list = ListInternal(ct)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new SnapshotSummary(
                s.Id, s.CreatedAtUtc, s.Origin, s.Target.Name))
            .ToList();

        return Task.FromResult(list);
    }

    public Task DeleteAllAsync(CancellationToken ct = default)
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }

    private List<PrinterSnapshot> ListInternal(CancellationToken ct)
    {
        var result = new List<PrinterSnapshot>();
        foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if (info.Length > MaxSizeBytes || info.Length == 0)
            {
                continue; // too corrupt or empty: skip in listings
            }

            try
            {
                var text = File.ReadAllText(file);
                var snap = Deserialize(text, null);
                if (snap is not null)
                {
                    result.Add(snap);
                }
            }
            catch (InvalidDataException)
            {
                // an invalid file does not take down the listing
            }
            catch (JsonException)
            {
                // same
            }
        }

        return result;
    }

    private static PrinterSnapshot? Deserialize(string text, Guid? expectedId)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(
                text,
                new JsonDocumentOptions { MaxDepth = 16, AllowTrailingCommas = false });
        }
        catch (JsonException ex)
        {
            // Malformed JSON or over the depth limit is a store error, not a framework one.
            throw new InvalidDataException("Snapshot contains invalid JSON or exceeds the depth limit.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("schemaVersion", out var version)
                || !root.TryGetProperty("id", out var idEl)
                || !root.TryGetProperty("createdAtUtc", out var createdEl)
                || !root.TryGetProperty("origin", out var originEl)
                || !root.TryGetProperty("target", out var targetEl)
                || targetEl.ValueKind != JsonValueKind.Object
                || !targetEl.TryGetProperty("name", out _)
                || !targetEl.TryGetProperty("protocol", out _))
            {
                throw new InvalidDataException("Snapshot missing minimum required fields.");
            }

            PrinterSnapshot? snap;
            try
            {
                snap = JsonSerializer.Deserialize<PrinterSnapshot>(text, ReadOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Snapshot content does not convert to the schema: {ex.Message}", ex);
            }

            if (snap is null)
            {
                throw new InvalidDataException("Empty snapshot after deserialization.");
            }

            if (expectedId is { } expected && snap.Id != expected)
            {
                throw new InvalidDataException("File id differs from the requested one.");
            }

            if (!string.Equals(snap.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"unsupported schemaVersion '{snap.SchemaVersion}'.");
            }

            return snap with
            {
                Permissions = snap.Permissions ?? new Dictionary<string, string>(),
                Defaults = snap.Defaults ?? new Dictionary<string, string>(),
            };
        }
    }

    private static void ValidateRequired(PrinterSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Target?.Name))
        {
            throw new InvalidDataException("target.name is required.");
        }
    }

    private string PathFor(Guid id)
    {
        // File name derived only from the Guid: printer names never touch the file system.
        return Path.Combine(_root, id.ToString("D") + ".json");
    }

    /// <summary>
    /// Enum converter for reading: accepts the PascalCase name, kebab-case
    /// and snake_case forms (produced on write with JsonNamingPolicy) and numeric values.
    /// Works for any enum type, not just SnapshotOrigin.
    /// </summary>
    private sealed class EnumKebabOrCamelConverter : JsonConverter<Enum>
    {
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert.IsEnum;

        public override Enum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return (Enum)Enum.ToObject(typeToConvert, reader.GetInt32());
            }

            var text = reader.GetString();
            if (text is null)
            {
                throw new JsonException($"Null value for enum {typeToConvert.Name}.");
            }

            // kebab-case / snake_case -> PascalCase ("pre-repair" => "PreRepair").
            var pascal = string.Concat(
                text.Split('-', '_')
                    .Where(static part => part.Length > 0)
                    .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));

            var type = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
            return (Enum)Enum.Parse(type, pascal, ignoreCase: true);
        }

        public override void Write(Utf8JsonWriter writer, Enum value, JsonSerializerOptions options)
            => throw new NotSupportedException("Read-only; writing uses WriteOptions.");
    }

    /// <summary>Writes SnapshotOrigin in kebab-case ("pre-repair"); other enums follow the default converter.</summary>
    private sealed class OriginKebabWriter : JsonConverter<SnapshotOrigin>
    {
        public override SnapshotOrigin Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("Write-only; reading uses EnumKebabOrCamelConverter.");

        public override void Write(Utf8JsonWriter writer, SnapshotOrigin value, JsonSerializerOptions options)
            => writer.WriteStringValue(value switch
            {
                SnapshotOrigin.Manual => "manual",
                SnapshotOrigin.PreRepair => "pre-repair",
                SnapshotOrigin.PostRepair => "post-repair",
                SnapshotOrigin.Scheduled => "scheduled",
                _ => value.ToString(),
            });
    }
}
