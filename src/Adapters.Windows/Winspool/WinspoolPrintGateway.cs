using System.Runtime.Versioning;
using PrinterRescue.Core;
using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Adapters.Windows.Winspool;

/// <summary>
/// Real gateway to the print subsystem via winspool.drv.
/// Every public entry is guarded by the platform check
/// (CA1416: TreatWarningsAsErrors enforces the pattern on each method).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinspoolPrintGateway : IPrintSystemGateway
{
    public Task<IReadOnlyList<PrinterTarget>> ListPrintersAsync(CancellationToken ct = default)
    {
        EnsureWindows();

        var names = WinspoolNative.EnumPrinterNames();
        var targets = new List<PrinterTarget>(names.Count);
        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            var info = WinspoolNative.GetPrinterInfo(name);
            var portName = info?.PortName;
            targets.Add(new PrinterTarget(
                Name: name,
                ShareName: info?.ShareName,
                PortName: portName,
                Protocol: Mappers.InferProtocol(portName),
                DeviceId: null,
                DriverName: info?.DriverName,
                DriverVersion: info?.DriverVersion));
        }

        return Task.FromResult<IReadOnlyList<PrinterTarget>>(targets);
    }

    public Task<PortConfig?> GetPortAsync(string portName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        EnsureWindows();

        // The canonical TCP port configuration is derived from the name (Windows convention);
        // local/USB ports have no network configuration to restore.
        return Task.FromResult(Mappers.DerivePortConfig(portName));
    }

    public Task<QueueState> GetQueueStateAsync(string queueName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        EnsureWindows();

        var exists = WinspoolNative.PrinterExists(queueName);
        if (!exists)
        {
            return Task.FromResult(new QueueState(queueName, Exists: false, StuckJobs: 0,
                DefaultPaperSize: null, CopiesDefault: 1, ColorDefault: false, DuplexDefault: false));
        }

        var jobs = WinspoolNative.GetJobs(queueName);
        var now = DateTime.UtcNow;
        var stuck = jobs.Count(j => Mappers.IsJobStuck(j.Status, j.SubmittedUtc, now));

        return Task.FromResult(new QueueState(queueName, Exists: true, StuckJobs: stuck,
            DefaultPaperSize: null, CopiesDefault: 1, ColorDefault: false, DuplexDefault: false));
    }

    public Task<DriverInfo?> GetDriverInfoAsync(string driverName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);
        EnsureWindows();

        var drivers = WinspoolNative.EnumDrivers();
        var found = drivers.FirstOrDefault(d =>
            string.Equals(d.Name, driverName, StringComparison.OrdinalIgnoreCase));

        if (found is { } d)
        {
            return Task.FromResult<DriverInfo?>(new DriverInfo(
                d.Name, d.Version ?? "unknown", d.InfName, PresentInDriverStore: true,
                Mappers.IsIppClassDriver(d.Name)));
        }

        // Driver named on the printer but missing from the local repository:
        // present on the system only if the printer using it is installed.
        return Task.FromResult<DriverInfo?>(new DriverInfo(
            driverName, "unknown", InfName: null,
            PresentInDriverStore: false, IsIppClassDriver: Mappers.IsIppClassDriver(driverName)));
    }

    public Task<IReadOnlyDictionary<string, string>> GetPermissionsSddlAsync(string queueName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        EnsureWindows();

        var sddl = WinspoolNative.GetSddl(queueName);
        IReadOnlyDictionary<string, string> map = string.IsNullOrEmpty(sddl)
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["queueSddl"] = sddl };
        return Task.FromResult(map);
    }

    public Task<IReadOnlyDictionary<string, string>> GetDefaultsAsync(string queueName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        EnsureWindows();

        var devMode = WinspoolNative.GetDevMode(queueName);
        IReadOnlyDictionary<string, string> defaults = devMode is { } dm
            ? Mappers.MapDefaults(dm.PaperSize, dm.Copies, dm.Color, dm.Duplex)
            : new Dictionary<string, string>();
        return Task.FromResult(defaults);
    }

    /// <summary>Platform guard shared by the adapters.</summary>
    internal static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Access to the Windows print subsystem requires running on Windows.");
        }
    }
}
