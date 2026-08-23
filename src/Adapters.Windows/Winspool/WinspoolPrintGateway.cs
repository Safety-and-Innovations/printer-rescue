using System.Runtime.Versioning;
using PrinterRescue.Core;
using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Adapters.Windows.Winspool;

/// <summary>
/// Gateway real do subsistema de impressão via winspool.drv.
/// Todas as entradas públicas são protegidas por guarda de plataforma
/// (CA1416: TreatWarningsAsErrors força o padrão em todo método).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinspoolPrintGateway : IPrintSystemGateway
{
    public Task<IReadOnlyList<PrinterTarget>> ListPrintersAsync(CancellationToken ct = default)
    {
        EnsureWindows();

        var nomes = WinspoolNative.EnumPrinterNames();
        var alvos = new List<PrinterTarget>(nomes.Count);
        foreach (var nome in nomes)
        {
            ct.ThrowIfCancellationRequested();
            var info = WinspoolNative.ObterInfoImpressora(nome);
            var portName = info?.PortName;
            alvos.Add(new PrinterTarget(
                Name: nome,
                ShareName: info?.ShareName,
                PortName: portName,
                Protocol: Mappers.InferirProtocolo(portName),
                DeviceId: null,
                DriverName: info?.DriverName,
                DriverVersion: info?.DriverVersion));
        }

        return Task.FromResult<IReadOnlyList<PrinterTarget>>(alvos);
    }

    public Task<PortConfig?> GetPortAsync(string portName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        EnsureWindows();

        // A configuração canônica da porta TCP é derivada do nome (convenção do Windows);
        // portas locais/USB não têm configuração de rede para restaurar.
        return Task.FromResult(Mappers.DerivarPortConfig(portName));
    }

    public Task<QueueState> GetQueueStateAsync(string queueName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        EnsureWindows();

        var existe = WinspoolNative.ImpressoraExiste(queueName);
        if (!existe)
        {
            return Task.FromResult(new QueueState(queueName, Exists: false, StuckJobs: 0,
                DefaultPaperSize: null, CopiesDefault: 1, ColorDefault: false, DuplexDefault: false));
        }

        var jobs = WinspoolNative.ObterJobs(queueName);
        var agora = DateTime.UtcNow;
        var presos = jobs.Count(j => Mappers.JobPreso(j.Status, j.SubmittedUtc, agora));

        return Task.FromResult(new QueueState(queueName, Exists: true, StuckJobs: presos,
            DefaultPaperSize: null, CopiesDefault: 1, ColorDefault: false, DuplexDefault: false));
    }

    public Task<DriverInfo?> GetDriverInfoAsync(string driverName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);
        EnsureWindows();

        var drivers = WinspoolNative.EnumDrivers();
        var encontrado = drivers.FirstOrDefault(d =>
            string.Equals(d.Nome, driverName, StringComparison.OrdinalIgnoreCase));

        if (encontrado is { } d)
        {
            return Task.FromResult<DriverInfo?>(new DriverInfo(
                d.Nome, d.Versao ?? "desconhecida", d.InfName, PresentInDriverStore: true,
                Mappers.EhIppClassDriver(d.Nome)));
        }

        // Driver nomeado na impressora mas ausente do repositório local:
        // presente no sistema apenas se a impressora que o usa está instalada.
        return Task.FromResult<DriverInfo?>(new DriverInfo(
            driverName, "desconhecida", InfName: null,
            PresentInDriverStore: false, IsIppClassDriver: Mappers.EhIppClassDriver(driverName)));
    }

    public Task<IReadOnlyDictionary<string, string>> GetPermissionsSddlAsync(string queueName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        EnsureWindows();

        var sddl = WinspoolNative.ObterSddl(queueName);
        IReadOnlyDictionary<string, string> mapa = string.IsNullOrEmpty(sddl)
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["queueSddl"] = sddl };
        return Task.FromResult(mapa);
    }

    public Task<IReadOnlyDictionary<string, string>> GetDefaultsAsync(string queueName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        EnsureWindows();

        var devMode = WinspoolNative.ObterDevMode(queueName);
        IReadOnlyDictionary<string, string> defaults = devMode is { } dm
            ? Mappers.MapearDefaults(dm.PaperSize, dm.Copies, dm.Color, dm.Duplex)
            : new Dictionary<string, string>();
        return Task.FromResult(defaults);
    }

    /// <summary>Guarda de plataforma compartilhada pelos adapters.</summary>
    internal static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "O acesso ao subsistema de impressão do Windows requer execução no Windows.");
        }
    }
}
