using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Win32;
using PrinterRescue.Core;
using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Adapters.Windows.Winspool;

/// <summary>
/// Real executor of repair actions on Windows.
/// RULE #1 enforced in code: no action downloads or installs external binaries —
/// reinstall uses only the driver name recorded in the snapshot
/// (which was present on the machine when it worked) or the Microsoft IPP Class Driver.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRepairExecutor : IRepairExecutor
{
    private const string IppClassDriverName = "Microsoft IPP Class Driver";

    public bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public Task<RepairOutcome> ExecuteAsync(RepairStep action, PrinterSnapshot context, CancellationToken ct = default)
    {
        WinspoolPrintGateway.EnsureWindows();
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            ct.ThrowIfCancellationRequested();
            return action.Kind switch
            {
                RepairActionKind.RestartSpooler => RestartSpooler(action, context),
                RepairActionKind.ClearQueue => ClearQueue(action, context),
                RepairActionKind.RestorePort => RestorePort(action, context),
                RepairActionKind.ReinstallWithIppClassDriver => Reinstall(action, context, IppClassDriverName),
                RepairActionKind.ReinstallFromDriverStore => Reinstall(action, context, context.Driver?.Name ?? IppClassDriverName),
                RepairActionKind.RemoveBrokenInstall => RemoveInstallation(action, context),
                RepairActionKind.RestorePermissions or RepairActionKind.RestoreDefaults =>
                    Task.FromResult(Failed(action, context, $"Step '{action.Kind}' is not yet supported in this release.")),
                _ => Task.FromResult(Failed(action, context, $"Unknown step '{action.Kind}'.")),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(new RepairOutcome(
                Guid.NewGuid(), context.Id, action.Kind, RepairStatus.Failed,
                $"Failed to run '{action.Kind}': {ex.Message}"));
        }
    }

    // ---- Actions --------------------------------------------------------------

    private static Task<RepairOutcome> RestartSpooler(RepairStep action, PrinterSnapshot context)
    {
        using var service = new ServiceController("Spooler");
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }

        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

        return Task.FromResult(Succeeded(action, context, "Print Spooler service restarted."));
    }

    private static Task<RepairOutcome> ClearQueue(RepairStep action, PrinterSnapshot context)
    {
        var name = context.Target.Name;
        if (!WinspoolNative.OpenPrinterW(name, out var handle, IntPtr.Zero))
        {
            return Task.FromResult(Failed(action, context, $"Queue '{name}' could not be opened."));
        }

        try
        {
            var jobs = WinspoolNative.GetJobs(name);
            var removed = 0;
            foreach (var job in jobs)
            {
                if (WinspoolNative.SetJobW(handle, job.JobId, 0, null, WinspoolNative.JobControlDelete))
                {
                    removed++;
                }
            }

            return Task.FromResult(Succeeded(action, context,
                $"Queue cleared: {removed} of {jobs.Count} jobs removed."));
        }
        finally
        {
            _ = WinspoolNative.ClosePrinter(handle);
        }
    }

    private static Task<RepairOutcome> RestorePort(RepairStep action, PrinterSnapshot context)
    {
        if (context.Port is not { } port)
        {
            return Task.FromResult(Failed(action, context, "Snapshot has no port configuration to restore."));
        }

        // The default Windows TCP/IP port is persisted in the registry by the port monitor.
        var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Print\Monitors\Standard TCP/IP Port\Ports", writable: true)
            ?? throw new Win32Exception("Default TCP/IP port monitor not found.");

        using (key)
        {
            var sub = key.CreateSubKey(port.PortName);
            using (sub)
            {
                sub.SetValue("HostName", port.HostAddress);
                sub.SetValue("IPAddress", port.HostAddress);
                sub.SetValue("PortNumber", port.PortNumber, RegistryValueKind.DWord);
                sub.SetValue("Protocol", 1, RegistryValueKind.DWord); // 1 = RAW
                sub.SetValue("SNMP Enabled", 0, RegistryValueKind.DWord);
            }
        }

        return Task.FromResult(Succeeded(action, context,
            $"Port '{port.PortName}' restored to {port.HostAddress}:{port.PortNumber}."));
    }

    private static async Task<RepairOutcome> Reinstall(RepairStep action, PrinterSnapshot context, string driverName)
    {
        var name = context.Target.Name;
        var portName = context.Port?.PortName ?? context.Target.PortName;
        var shareName = context.Target.ShareName;

        // Remove the broken installation while keeping the snapshot (already guaranteed by the workflow).
        if (WinspoolNative.OpenPrinterW(name, out var handle,
                new IntPtr((long)(WinspoolNative.PrinterAccessUse | WinspoolNative.PrinterAccessAdminister))))
        {
            _ = WinspoolNative.DeletePrinter(handle);
            _ = WinspoolNative.ClosePrinter(handle);
        }

        var info = new WinspoolNative.PRINTER_INFO_2W
        {
            pPrinterName = Marshal.StringToHGlobalUni(name),
            pPortName = Marshal.StringToHGlobalUni(portName ?? "FILE:"),
            pDriverName = Marshal.StringToHGlobalUni(driverName),
            pShareName = shareName is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(shareName),
            pComment = IntPtr.Zero,
            pLocation = IntPtr.Zero,
            pDevMode = IntPtr.Zero,
            pSecDesc = IntPtr.Zero,
            Attributes = 0,
        };

        try
        {
            var created = WinspoolNative.AddPrinterW(null, 2, ref info);
            if (created == IntPtr.Zero)
            {
                return await Task.FromResult(Failed(action, context,
                    $"Reinstall failed (local driver '{driverName}' may no longer be available)."));
            }

            return await Task.FromResult(Succeeded(action, context,
                $"Printer reinstalled with local driver '{driverName}' (no external download)."));
        }
        finally
        {
            Marshal.FreeHGlobal(info.pPrinterName);
            Marshal.FreeHGlobal(info.pPortName);
            Marshal.FreeHGlobal(info.pDriverName);
            if (info.pShareName != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(info.pShareName);
            }
        }
    }

    private static Task<RepairOutcome> RemoveInstallation(RepairStep action, PrinterSnapshot context)
    {
        var name = context.Target.Name;
        if (!WinspoolNative.OpenPrinterW(name, out var handle,
                new IntPtr((long)(WinspoolNative.PrinterAccessUse | WinspoolNative.PrinterAccessAdminister))))
        {
            return Task.FromResult(Failed(action, context, $"Printer '{name}' not found for removal."));
        }

        try
        {
            if (!WinspoolNative.DeletePrinter(handle))
            {
                return Task.FromResult(Failed(action, context, $"Removal of '{name}' failed (check privileges)."));
            }

            return Task.FromResult(Succeeded(action, context, $"Broken installation of '{name}' removed."));
        }
        finally
        {
            _ = WinspoolNative.ClosePrinter(handle);
        }
    }

    // ---- Helpers ------------------------------------------------------------

    private static RepairOutcome Succeeded(RepairStep action, PrinterSnapshot context, string detail) =>
        new(Guid.NewGuid(), context.Id, action.Kind, RepairStatus.Applied, detail);

    private static RepairOutcome Failed(RepairStep action, PrinterSnapshot context, string detail) =>
        new(Guid.NewGuid(), context.Id, action.Kind, RepairStatus.Failed, detail);
}
