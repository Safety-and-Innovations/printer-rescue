using System.Text.Json;
using System.Text.Json.Serialization;
using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Snapshots;

/// <summary>
/// Persistência de snapshots em arquivos JSON — um por snapshot, nomeado pelo Id.
/// Parser endurecido: tamanho máximo 1 MiB, profundidade máxima 16, sem traversal
/// (nomes de impressora nunca viram caminho).
/// </summary>
public sealed class SnapshotStore : ISnapshotStore
{
    public const string SchemaVersionAtual = "1.0";
    private const long TamanhoMaximoBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions OpcoesEscrita = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            // SnapshotOrigin em kebab-case ("pre-repair"); demais enums em snake_case
            // ("tcp_raw"). O primeiro conversor compatível vence na escrita.
            new OriginKebabWriter(),
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower),
        },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions OpcoesLeitura = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 16,
        Converters =
        {
            // Único conversor de enums na leitura: aceita PascalCase, kebab-case
            // ("pre-repair"), snake_case ("pre_repair") e numérico — o conjunto que
            // a própria escrita produz. Não registrar JsonStringEnumConverter junto:
            // o primeiro da lista vence e rejeitaria as formas kebab/snake.
            new EnumKebabOuCamelConverter(),
        },
    };

    private readonly string _raiz;

    public SnapshotStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _raiz = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_raiz);
    }

    /// <summary>Raiz física dos snapshots (para diagnóstico e testes).</summary>
    public string RootDirectory => _raiz;

    public async Task SaveAsync(PrinterSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!string.Equals(snapshot.SchemaVersion, SchemaVersionAtual, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"schemaVersion '{snapshot.SchemaVersion}' desconhecida; esperada '{SchemaVersionAtual}'.");
        }

        ValidarObrigatorios(snapshot);

        var caminho = CaminhoDo(snapshot.Id);
        await File.WriteAllTextAsync(caminho, JsonSerializer.Serialize(snapshot, OpcoesEscrita), ct)
            .ConfigureAwait(false);
    }

    public async Task<PrinterSnapshot?> LoadAsync(Guid id, CancellationToken ct = default)
    {
        var caminho = CaminhoDo(id);
        if (!File.Exists(caminho))
        {
            return null;
        }

        var info = new FileInfo(caminho);
        if (info.Length > TamanhoMaximoBytes)
        {
            throw new InvalidDataException($"Snapshot {id} excede o limite de 1 MiB.");
        }

        var texto = await File.ReadAllTextAsync(caminho, ct).ConfigureAwait(false);
        return Desserializar(texto, id);
    }

    public Task<PrinterSnapshot?> FindLatestForAsync(string printerName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        var maisRecente = ListarInternamente(ct)
            .Where(s => string.Equals(s.Target.Name, printerName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => (PrinterSnapshot?)s)
            .FirstOrDefault();

        return Task.FromResult(maisRecente);
    }

    public Task<IReadOnlyList<SnapshotSummary>> ListAsync(CancellationToken ct = default)
    {
        IReadOnlyList<SnapshotSummary> lista = ListarInternamente(ct)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new SnapshotSummary(
                s.Id, s.CreatedAtUtc, s.Origin, s.Target.Name))
            .ToList();

        return Task.FromResult(lista);
    }

    public Task DeleteAllAsync(CancellationToken ct = default)
    {
        foreach (var arquivo in Directory.EnumerateFiles(_raiz, "*.json"))
        {
            File.Delete(arquivo);
        }

        return Task.CompletedTask;
    }

    private List<PrinterSnapshot> ListarInternamente(CancellationToken ct)
    {
        var resultado = new List<PrinterSnapshot>();
        foreach (var arquivo in Directory.EnumerateFiles(_raiz, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(arquivo);
            if (info.Length > TamanhoMaximoBytes || info.Length == 0)
            {
                continue; // corrompido demais ou vazio: ignora na listagem
            }

            try
            {
                var texto = File.ReadAllText(arquivo);
                var snap = Desserializar(texto, null);
                if (snap is not null)
                {
                    resultado.Add(snap);
                }
            }
            catch (InvalidDataException)
            {
                // arquivo inválido não derruba a listagem
            }
            catch (JsonException)
            {
                // idem
            }
        }

        return resultado;
    }

    private static PrinterSnapshot? Desserializar(string texto, Guid? idEsperado)
    {
        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(
                texto,
                new JsonDocumentOptions { MaxDepth = 16, AllowTrailingCommas = false });
        }
        catch (JsonException ex)
        {
            // JSON malformado ou acima do limite de profundidade é erro da loja, não do framework.
            throw new InvalidDataException("Snapshot contém JSON inválido ou acima do limite de profundidade.", ex);
        }

        using (documento)
        {
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object
                || !raiz.TryGetProperty("schemaVersion", out var versao)
                || !raiz.TryGetProperty("id", out var idEl)
                || !raiz.TryGetProperty("createdAtUtc", out var criadoEl)
                || !raiz.TryGetProperty("origin", out var origemEl)
                || !raiz.TryGetProperty("target", out var alvoEl)
                || alvoEl.ValueKind != JsonValueKind.Object
                || !alvoEl.TryGetProperty("name", out _)
                || !alvoEl.TryGetProperty("protocol", out _))
            {
                throw new InvalidDataException("Snapshot sem os campos obrigatórios mínimos.");
            }

            PrinterSnapshot? snap;
            try
            {
                snap = JsonSerializer.Deserialize<PrinterSnapshot>(texto, OpcoesLeitura);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Conteúdo de snapshot não converte para o schema: {ex.Message}", ex);
            }

            if (snap is null)
            {
                throw new InvalidDataException("Snapshot vazio após desserialização.");
            }

            if (idEsperado is { } esperado && snap.Id != esperado)
            {
                throw new InvalidDataException("Id do arquivo difere do solicitado.");
            }

            if (!string.Equals(snap.SchemaVersion, SchemaVersionAtual, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"schemaVersion '{snap.SchemaVersion}' não suportada.");
            }

            return snap with
            {
                Permissions = snap.Permissions ?? new Dictionary<string, string>(),
                Defaults = snap.Defaults ?? new Dictionary<string, string>(),
            };
        }
    }

    private static void ValidarObrigatorios(PrinterSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Target?.Name))
        {
            throw new InvalidDataException("target.name é obrigatório.");
        }
    }

    private string CaminhoDo(Guid id)
    {
        // Nome de arquivo derivado apenas do Guid: nomes de impressora nunca tocam o sistema de arquivos.
        return Path.Combine(_raiz, id.ToString("D") + ".json");
    }

    /// <summary>
    /// Conversor de enum para leitura: aceita o nome PascalCase, as formas kebab-case
    /// e snake_case (produzidas pela escrita com JsonNamingPolicy) e valor numérico.
    /// Funciona para qualquer tipo de enum, não apenas SnapshotOrigin.
    /// </summary>
    private sealed class EnumKebabOuCamelConverter : JsonConverter<Enum>
    {
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert.IsEnum;

        public override Enum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return (Enum)Enum.ToObject(typeToConvert, reader.GetInt32());
            }

            var texto = reader.GetString();
            if (texto is null)
            {
                throw new JsonException($"Valor nulo para enum {typeToConvert.Name}.");
            }

            // kebab-case / snake_case -> PascalCase ("pre-repair" => "PreRepair").
            var pascal = string.Concat(
                texto.Split('-', '_')
                    .Where(static parte => parte.Length > 0)
                    .Select(static parte => char.ToUpperInvariant(parte[0]) + parte[1..]));

            var tipo = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
            return (Enum)Enum.Parse(tipo, pascal, ignoreCase: true);
        }

        public override void Write(Utf8JsonWriter writer, Enum value, JsonSerializerOptions options)
            => throw new NotSupportedException("Somente leitura; a escrita usa OpcoesEscrita.");
    }

    /// <summary>Escreve SnapshotOrigin em kebab-case ("pre-repair"); demais enums seguem o conversor padrão.</summary>
    private sealed class OriginKebabWriter : JsonConverter<SnapshotOrigin>
    {
        public override SnapshotOrigin Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("Somente escrita; a leitura usa EnumKebabOuCamelConverter.");

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
