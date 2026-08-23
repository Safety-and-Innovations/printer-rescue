# Printer Rescue — Contratos Compartilhados (CONTRATOS.md)

> **Versão 1.0.0 · CONGELADO** para a rodada de implementação paralela.
> Qualquer mudança aqui exige sincronização com todos os agentes ativos.
> Autor: André Santo (forg3) | junkyardgoodies.app

Este documento define os tipos, enums e interfaces compartilhados entre os
módulos do Printer Rescue. Os agentes devem implementar exatamente estes
nomes, namespaces e assinaturas, sob pena de conflito de merge.

---

## 1. Namespaces

| Projeto | Namespace raiz |
|---|---|
| Core | `PrinterRescue.Core` |
| Adapters.Windows | `PrinterRescue.Adapters.Windows` |
| Cli | `PrinterRescue.Cli` |
| Gui | `PrinterRescue.Gui` |

## 2. Enums (namespace `PrinterRescue.Core`)

```csharp
public enum PrinterProtocol { TcpRaw, Lpr, Ipp, Usb, Wsd }

// Diagnóstico determinístico, de cima para baixo (README da ideia §Fluxo)
public enum CheckId
{
    SpoolerRunning,
    PortOpen,
    DriverPresent,
    QueueExists,
    QueueNotStuck,
    NoDuplicateInstall
}

public enum CheckResult { Pass, Fail, Warn, NotApplicable }

public enum Severity { Info, Warning, Error }

public enum RepairActionKind
{
    RestartSpooler,
    ClearQueue,
    ReinstallWithIppClassDriver,
    ReinstallFromDriverStore,
    RemoveBrokenInstall,
    RestorePort,
    RestorePermissions,
    RestoreDefaults
}

public enum RepairStatus { Applied, Failed, SkippedPolicyViolation, SkippedNoSnapshot }

public enum SnapshotOrigin { Manual, PreRepair, PostRepair, Scheduled }

public enum ExitCode
{
    Ok = 0,
    DiagnosticFailed = 2,
    RepairApplied = 4,
    RepairBlockedByPolicy = 6,
    SnapshotMissing = 8,
    InvalidArguments = 16,
    UnhandledError = 32
}
```

## 3. Records (namespace `PrinterRescue.Core`)

```csharp
public sealed record PrinterTarget(
    string Name,
    string? ShareName,
    string? PortName,
    PrinterProtocol Protocol,
    string? DeviceId,
    string? DriverName,
    string? DriverVersion);

public sealed record PortConfig(
    string PortName,
    string HostAddress,
    int PortNumber,
    PrinterProtocol Protocol);

public sealed record QueueState(
    string Name,
    bool Exists,
    int StuckJobs,
    string? DefaultPaperSize,
    int CopiesDefault,
    bool ColorDefault,
    bool DuplexDefault);

public sealed record DriverInfo(
    string Name,
    string Version,
    string? InfName,
    bool PresentInDriverStore,
    bool IsIppClassDriver);

public sealed record PrinterSnapshot(
    Guid Id,
    DateTime CreatedAtUtc,
    SnapshotOrigin Origin,
    PrinterTarget Target,
    PortConfig? Port,
    QueueState? Queue,
    DriverInfo? Driver,
    IReadOnlyDictionary<string, string> Permissions,
    IReadOnlyDictionary<string, string> Defaults,
    string SchemaVersion);

public sealed record SnapshotSummary(
    Guid Id,
    DateTime CreatedAtUtc,
    SnapshotOrigin Origin,
    string PrinterName);

public sealed record CheckOutcome(
    CheckId Id,
    CheckResult Result,
    Severity Severity,
    string Detail);

public sealed record RepairStep(
    RepairActionKind Kind,
    string Description,
    bool Destructive,
    bool RequiresElevation);

public sealed record RepairPlan(
    Guid TargetId,
    IReadOnlyList<RepairStep> Steps,
    bool RequiresElevation);

public sealed record RepairOutcome(
    Guid TargetId,
    Guid SnapshotId,
    RepairActionKind Kind,
    RepairStatus Status,
    string Detail);
```

## 4. Interfaces (namespace `PrinterRescue.Core.Interfaces`)

```csharp
/// <summary>Acesso ao estado real do subsistema de impressão.</summary>
public interface IPrintSystemGateway
{
    Task<IReadOnlyList<PrinterTarget>> ListPrintersAsync(CancellationToken ct = default);
    Task<PortConfig?> GetPortAsync(string portName, CancellationToken ct = default);
    Task<QueueState> GetQueueStateAsync(string queueName, CancellationToken ct = default);
    Task<DriverInfo?> GetDriverInfoAsync(string driverName, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetPermissionsSddlAsync(string queueName, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetDefaultsAsync(string queueName, CancellationToken ct = default);
}

/// <summary>Captura e persiste snapshots.</summary>
public interface ISnapshotStore
{
    Task SaveAsync(PrinterSnapshot snapshot, CancellationToken ct = default);
    Task<PrinterSnapshot?> FindLatestForAsync(string printerName, CancellationToken ct = default);
    Task<IReadOnlyList<SnapshotSummary>> ListAsync(CancellationToken ct = default);
    Task<PrinterSnapshot?> LoadAsync(Guid id, CancellationToken ct = default);
    Task DeleteAllAsync(CancellationToken ct = default);
}

/// <summary>Executa ações no Windows.</summary>
public interface IRepairExecutor
{
    bool IsElevated();
    Task<RepairOutcome> ExecuteAsync(RepairStep step, PrinterSnapshot context, CancellationToken ct = default);
}

/// <summary>Verificação determinística individual.</summary>
public interface IDiagnosticCheck
{
    CheckId Id { get; }
    Task<CheckOutcome> RunAsync(IPrintSystemGateway gateway, PrinterTarget target, CancellationToken ct = default);
}

public interface IDiagnosticEngine
{
    Task<DiagnosticReport> DiagnoseAndPlanAsync(PrinterTarget target, CancellationToken ct = default);
}

public interface IRepairPlanner
{
    RepairPlan PlanRepairs(DiagnosticReport report, PrinterSnapshot? lastGood);
}

public sealed record PolicyDecision(bool Allowed, string Reason);

/// <summary>Guarda de conformidade: bloqueia distribuição de driver (REGRA Nº 1).</summary>
public interface IPolicyGuard
{
    PolicyDecision Evaluate(RepairStep step, PrinterSnapshot context);
}
```

### Semântica dos checks (contrato comportamental)

- `SpoolerRunning`: Pass se o serviço de spooler está acessível/rodando; Fail caso contrário; os demais checks ficam `NotApplicable` quando este falha.
- `PortOpen`: Pass se porta TCP abre em ≤ 2 s; Fail em timeout/recusa; `NotApplicable` para USB/WSD.
- `DriverPresent`: Pass se o driver está presente no sistema ou DriverStore.
- `QueueExists`: Pass se a fila existe e está configurada.
- `QueueNotStuck`: Warn com N jobs presos; Pass com fila livre.
- `NoDuplicateInstall`: Fail quando há mais de uma instalação da mesma impressora.

### Semântica do `IRepairPlanner`

- Entrada: relatório com ≥ 1 check Fail + último snapshot válido da impressora.
- Ordem do plano: limpar fila travada → reiniciar spooler → restaurar porta/fila → reinstalar driver (IPP Class Driver preferido) → remover instalação quebrada.
- Sem snapshot: plano só com passos não-destrutivos; passos destrutivos resultam em `SkippedNoSnapshot`.

## 5. Formato do snapshot em disco

Caminho: `%ProgramData%\PrinterRescue\snapshots\<id>.json` — um arquivo por snapshot, UTF-8.

Exemplo:

```json
{
  "schemaVersion": "1.0",
  "id": "0f8b6c1e-5e2a-4a7b-9c3d-1f2e3a4b5c6d",
  "createdAtUtc": "2026-08-23T12:00:00Z",
  "origin": "pre-repair",
  "target": {
    "name": "HP LaserJet Pro",
    "shareName": null,
    "portName": "IP_192.168.0.40",
    "protocol": "tcp_raw",
    "deviceId": null,
    "driverName": "HP Universal PCL6",
    "driverVersion": "3.12.0.0"
  },
  "port": {
    "portName": "IP_192.168.0.40",
    "hostAddress": "192.168.0.40",
    "portNumber": 9100,
    "protocol": "tcp_raw"
  },
  "queue": {
    "name": "HP LaserJet Pro",
    "exists": true,
    "stuckJobs": 0,
    "defaultPaperSize": "A4",
    "copiesDefault": 1,
    "colorDefault": false,
    "duplexDefault": true
  },
  "driver": {
    "name": "HP Universal PCL6",
    "version": "3.12.0.0",
    "infName": "hpcu270u.inf",
    "presentInDriverStore": true,
    "isIppClassDriver": false
  },
  "permissions": { "queueSddl": "G:SYD:PAI(A;;FA;;;BA)" },
  "defaults": { "paperSize": "A4", "copies": "1", "color": "false", "duplex": "true" }
}
```

Regras de serialização:

- JSON camelCase, `JsonSerializerOptions` com `WriteIndented = true`.
- Enum `protocol` serializa como `tcp_raw`, `lpr`, `ipp`, `usb`, `wsd` (JsonStringEnumConverter com naming snake_case).
- Enum `origin` serializa como `manual`, `pre-repair`, `post-repair`, `scheduled` — usar JsonPropertyName nos membros.
- Campos obrigatórios mínimos: `schemaVersion`, `id`, `createdAtUtc`, `origin`, `target.name`, `target.protocol`.
- Desserialização tolerante a campos ausentes opcionais (tudo fora dos obrigatórios é anulável).
- Limite de tamanho ao ler: rejeitar arquivos > 1 MiB; profundidade máx. 16 (defesa contra JSON malicioso).

## 6. Regras transversais (obrigatórias para todos os agentes)

1. **TDD estrito**: nenhum código sem teste falho primeiro (RED→GREEN→REFACTOR), vertical slices.
2. **`TreatWarningsAsErrors`**: build com zero warnings.
3. **Assíncrono com `CancellationToken`** em toda operação de E/S; sem `async void`, sem `.Result`/`.Wait()`, sem `Thread.Sleep` em produção.
4. **Injeção de dependência via construtor**; interfaces no Core; adapters injetam gateways.
5. **Nada de código específico de Windows no Core** — Core roda em Linux/macOS/Windows e os testes do Core rodam no Linux deste host.
6. **REGRA Nº 1 é código**: o `IPolicyGuard` bloqueia qualquer passo que envolva download/hospedagem/distribuição de driver. Teste obrigatório: passo que instale driver externo → `SkippedPolicyViolation`.
7. **Destrutivo exige snapshot prévio**: sem snapshot, passos destrutivos viram `SkippedNoSnapshot`. Teste obrigatório.
8. **Segurança**: sem secrets no código; caminhos validados contra path traversal; limites de tamanho/profundidade no parser de snapshot; log sem PII além do tecnicamente necessário (nome de fila/IP são necessários).
9. **Commits descritivos pequenos** na branch própria, um commit por ciclo RED→GREEN concluído.
10. **Zero dependência externa no Core** (apenas BCL). Adapters: BCL + P/Invoke declarado localmente. CLI: System.CommandLine. GUI: Avalonia.
11. **Localização**: strings de usuário em pt-BR; identificadores/códigos em inglês.

## 7. Limites de cada agente

- Editar somente `src/<seu-projeto>/` e `tests/<SeuProjeto>.Tests/`.
- Não editar `Directory.Build.props`, `Directory.Build.targets`, solução, CI, README, LICENSE, docs/.
- Não criar projetos novos nem alterar versões de pacotes.
- Não implementar catálogo/base de drivers por modelo — REGRA Nº 1.
