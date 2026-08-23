using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Win32;
using PrinterRescue.Core;
using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Adapters.Windows.Winspool;

/// <summary>
/// Executor real das ações de reparo no Windows.
/// REGRA Nº 1 aplicada em código: nenhuma ação baixa ou instala binário externo —
/// reinstalação usa exclusivamente o nome do driver gravado no snapshot
/// (que estava presente na máquina quando funcionava) ou o Microsoft IPP Class Driver.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRepairExecutor : IRepairExecutor
{
    private const string NomeIppClassDriver = "Microsoft IPP Class Driver";

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
                RepairActionKind.RestartSpooler => ReiniciarSpooler(action, context),
                RepairActionKind.ClearQueue => LimparFila(action, context),
                RepairActionKind.RestorePort => RestaurarPorta(action, context),
                RepairActionKind.ReinstallWithIppClassDriver => Reinstalar(action, context, NomeIppClassDriver),
                RepairActionKind.ReinstallFromDriverStore => Reinstalar(action, context, context.Driver?.Name ?? NomeIppClassDriver),
                RepairActionKind.RemoveBrokenInstall => RemoverInstalacao(action, context),
                RepairActionKind.RestorePermissions or RepairActionKind.RestoreDefaults =>
                    Task.FromResult(Falhou(action, context, $"Passo '{action.Kind}' ainda não suportado neste release.")),
                _ => Task.FromResult(Falhou(action, context, $"Passo '{action.Kind}' desconhecido.")),
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
                $"Falha ao executar '{action.Kind}': {ex.Message}"));
        }
    }

    // ---- Ações --------------------------------------------------------------

    private static Task<RepairOutcome> ReiniciarSpooler(RepairStep action, PrinterSnapshot context)
    {
        using var servico = new ServiceController("Spooler");
        if (servico.Status != ServiceControllerStatus.Stopped)
        {
            servico.Stop();
            servico.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }

        servico.Start();
        servico.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

        return Task.FromResult(Aplicado(action, context, "Serviço Print Spooler reiniciado."));
    }

    private static Task<RepairOutcome> LimparFila(RepairStep action, PrinterSnapshot context)
    {
        var nome = context.Target.Name;
        if (!WinspoolNative.OpenPrinterW(nome, out var handle, IntPtr.Zero))
        {
            return Task.FromResult(Falhou(action, context, $"Fila '{nome}' não pôde ser aberta."));
        }

        try
        {
            var jobs = WinspoolNative.ObterJobs(nome);
            var removidos = 0;
            foreach (var job in jobs)
            {
                if (WinspoolNative.SetJobW(handle, job.JobId, 0, null, WinspoolNative.JobControlDelete))
                {
                    removidos++;
                }
            }

            return Task.FromResult(Aplicado(action, context,
                $"Fila limpa: {removidos} de {jobs.Count} trabalhos removidos."));
        }
        finally
        {
            _ = WinspoolNative.ClosePrinter(handle);
        }
    }

    private static Task<RepairOutcome> RestaurarPorta(RepairStep action, PrinterSnapshot context)
    {
        if (context.Port is not { } porta)
        {
            return Task.FromResult(Falhou(action, context, "Snapshot não contém configuração de porta para restaurar."));
        }

        // A porta TCP/IP padrão do Windows é persistida no registro pelo monitor de porta.
        var chave = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Print\Monitors\Standard TCP/IP Port\Ports", writable: true)
            ?? throw new Win32Exception("Monitor de porta TCP/IP padrão não encontrado.");

        using (chave)
        {
            var sub = chave.CreateSubKey(porta.PortName);
            using (sub)
            {
                sub.SetValue("HostName", porta.HostAddress);
                sub.SetValue("IPAddress", porta.HostAddress);
                sub.SetValue("PortNumber", porta.PortNumber, RegistryValueKind.DWord);
                sub.SetValue("Protocol", 1, RegistryValueKind.DWord); // 1 = RAW
                sub.SetValue("SNMP Enabled", 0, RegistryValueKind.DWord);
            }
        }

        return Task.FromResult(Aplicado(action, context,
            $"Porta '{porta.PortName}' restaurada para {porta.HostAddress}:{porta.PortNumber}."));
    }

    private static async Task<RepairOutcome> Reinstalar(RepairStep action, PrinterSnapshot context, string driverName)
    {
        var nome = context.Target.Name;
        var portaNome = context.Port?.PortName ?? context.Target.PortName;
        var shareName = context.Target.ShareName;

        // Remove a instalação quebrada preservando o snapshot (já garantido pela workflow).
        if (WinspoolNative.OpenPrinterW(nome, out var handle,
                new IntPtr((long)(WinspoolNative.PrinterAccessUse | WinspoolNative.PrinterAccessAdminister))))
        {
            _ = WinspoolNative.DeletePrinter(handle);
            _ = WinspoolNative.ClosePrinter(handle);
        }

        var info = new WinspoolNative.PRINTER_INFO_2W
        {
            pPrinterName = Marshal.StringToHGlobalUni(nome),
            pPortName = Marshal.StringToHGlobalUni(portaNome ?? "FILE:"),
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
            var criada = WinspoolNative.AddPrinterW(null, 2, ref info);
            if (criada == IntPtr.Zero)
            {
                return await Task.FromResult(Falhou(action, context,
                    $"Reinstalação falhou (driver local '{driverName}' pode não estar mais disponível)."));
            }

            return await Task.FromResult(Aplicado(action, context,
                $"Impressora reinstalada com o driver local '{driverName}' (sem download externo)."));
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

    private static Task<RepairOutcome> RemoverInstalacao(RepairStep action, PrinterSnapshot context)
    {
        var nome = context.Target.Name;
        if (!WinspoolNative.OpenPrinterW(nome, out var handle,
                new IntPtr((long)(WinspoolNative.PrinterAccessUse | WinspoolNative.PrinterAccessAdminister))))
        {
            return Task.FromResult(Falhou(action, context, $"Impressora '{nome}' não encontrada para remoção."));
        }

        try
        {
            if (!WinspoolNative.DeletePrinter(handle))
            {
                return Task.FromResult(Falhou(action, context, $"Remoção de '{nome}' falhou (verifique privilégios)."));
            }

            return Task.FromResult(Aplicado(action, context, $"Instalação quebrada de '{nome}' removida."));
        }
        finally
        {
            _ = WinspoolNative.ClosePrinter(handle);
        }
    }

    // ---- Helpers ------------------------------------------------------------

    private static RepairOutcome Aplicado(RepairStep action, PrinterSnapshot context, string detalhe) =>
        new(Guid.NewGuid(), context.Id, action.Kind, RepairStatus.Applied, detalhe);

    private static RepairOutcome Falhou(RepairStep action, PrinterSnapshot context, string detalhe) =>
        new(Guid.NewGuid(), context.Id, action.Kind, RepairStatus.Failed, detalhe);
}
