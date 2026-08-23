using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Tests;

/// <summary>Gateway falso para testes: comportamento configurável por chamada.</summary>
public sealed class FakePrintGateway : IPrintSystemGateway
{
    private readonly IReadOnlyList<PrinterTarget> _printers;
    private readonly PortConfig? _port;
    private readonly QueueState _queue;
    private readonly DriverInfo? _driver;

    public FakePrintGateway(
        IReadOnlyList<PrinterTarget>? printers = null,
        PortConfig? port = null,
        QueueState? queue = null,
        DriverInfo? driver = null)
    {
        _printers = printers ?? [];
        _port = port;
        _queue = queue ?? new QueueState("Fila", Exists: true, StuckJobs: 0, DefaultPaperSize: "A4", CopiesDefault: 1, ColorDefault: false, DuplexDefault: true);
        _driver = driver;
    }

    /// <summary>Quando definido, ListPrintersAsync lança esta exceção (spooler inacessível).</summary>
    public Exception? ListPrintersError { get; init; }

    public int ListPrintersCalls { get; private set; }

    public Task<IReadOnlyList<PrinterTarget>> ListPrintersAsync(CancellationToken ct = default)
    {
        ListPrintersCalls++;
        if (ListPrintersError is not null)
        {
            throw ListPrintersError;
        }

        return Task.FromResult(_printers);
    }

    public Task<PortConfig?> GetPortAsync(string portName, CancellationToken ct = default)
        => Task.FromResult(_port);

    public Task<QueueState> GetQueueStateAsync(string queueName, CancellationToken ct = default)
        => Task.FromResult(_queue);

    public Task<DriverInfo?> GetDriverInfoAsync(string driverName, CancellationToken ct = default)
        => Task.FromResult(_driver);

    public Task<IReadOnlyDictionary<string, string>> GetPermissionsSddlAsync(string queueName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

    public Task<IReadOnlyDictionary<string, string>> GetDefaultsAsync(string queueName, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}

/// <summary>Fábrica de fixtures para testes de diagnóstico.</summary>
public static class TestTargets
{
    public const string NomePadrao = "HP LaserJet Pro";

    public static PrinterTarget Tcp(string name = NomePadrao, string? portName = "IP_192.168.0.40")
        => new(Name: name, ShareName: null, PortName: portName, Protocol: PrinterProtocol.TcpRaw,
               DeviceId: null, DriverName: "HP Universal PCL6", DriverVersion: "3.12.0.0");

    public static PrinterTarget Usb(string name = NomePadrao)
        => new(Name: name, ShareName: null, PortName: "USB001", Protocol: PrinterProtocol.Usb,
               DeviceId: null, DriverName: "HP Universal PCL6", DriverVersion: "3.12.0.0");

    public static PrinterTarget Wsd(string name = NomePadrao)
        => new(Name: name, ShareName: null, PortName: "WSD-001", Protocol: PrinterProtocol.Wsd,
               DeviceId: null, DriverName: "HP Universal PCL6", DriverVersion: "3.12.0.0");
}
