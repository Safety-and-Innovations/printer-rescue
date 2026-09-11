using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Workflows;

/// <summary>
/// Captures the current functional state of a printer from the gateway,
/// producing a complete snapshot (port, queue, driver, permissions, defaults).
/// </summary>
public sealed class CaptureWorkflow
{
    private readonly IPrintSystemGateway _gateway;

    public CaptureWorkflow(IPrintSystemGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        _gateway = gateway;
    }

    public async Task<PrinterSnapshot> CaptureAsync(PrinterTarget target, SnapshotOrigin origin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var port = target.PortName is { } portName
            ? await _gateway.GetPortAsync(portName, ct).ConfigureAwait(false)
            : null;

        var queue = await _gateway.GetQueueStateAsync(target.Name, ct).ConfigureAwait(false);

        var driver = target.DriverName is { } driverName
            ? await _gateway.GetDriverInfoAsync(driverName, ct).ConfigureAwait(false)
            : null;

        var permissions = await _gateway.GetPermissionsSddlAsync(target.Name, ct).ConfigureAwait(false);
        var defaults = await _gateway.GetDefaultsAsync(target.Name, ct).ConfigureAwait(false);

        return new PrinterSnapshot(
            Id: Guid.NewGuid(),
            CreatedAtUtc: DateTime.UtcNow,
            Origin: origin,
            Target: target,
            Port: port,
            Queue: queue,
            Driver: driver,
            Permissions: permissions,
            Defaults: defaults,
            SchemaVersion: Snapshots.SnapshotStore.CurrentSchemaVersion);
    }
}
