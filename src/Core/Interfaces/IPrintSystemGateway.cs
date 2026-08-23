namespace PrinterRescue.Core.Interfaces;

/// <summary>Acesso ao estado real do subsistema de impressão.</summary>
public interface IPrintSystemGateway
{
    Task<IReadOnlyList<global::PrinterRescue.Core.PrinterTarget>> ListPrintersAsync(CancellationToken ct = default);

    Task<global::PrinterRescue.Core.PortConfig?> GetPortAsync(string portName, CancellationToken ct = default);

    Task<global::PrinterRescue.Core.QueueState> GetQueueStateAsync(string queueName, CancellationToken ct = default);

    Task<global::PrinterRescue.Core.DriverInfo?> GetDriverInfoAsync(string driverName, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>> GetPermissionsSddlAsync(string queueName, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>> GetDefaultsAsync(string queueName, CancellationToken ct = default);
}
