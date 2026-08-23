using System.Text.Json;
using PrinterRescue.Adapters.Windows.Winspool;
using PrinterRescue.Core;
using PrinterRescue.Core.Diagnostics;
using PrinterRescue.Core.Diagnostics.Checks;
using PrinterRescue.Core.Interfaces;
using PrinterRescue.Core.Policy;
using PrinterRescue.Core.Repair;
using PrinterRescue.Core.Snapshots;
using PrinterRescue.Core.Workflows;

namespace PrinterRescue.Cli;

/// <summary>
/// Fachada fina do motor: composição do grafo de objetos e comandos
/// list / snapshot / diagnose / repair / snapshots.
/// Em não-Windows, apenas `snapshots list` opera (loca e multiplataforma).
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Ajuda();
                return (int)ExitCode.InvalidArguments;
            }

            var raizSnapshots = CaminhoSnapshots();
            var store = new SnapshotStore(raizSnapshots);

            switch (args[0].ToLowerInvariant())
            {
                case "list":
                    return await CmdListAsync(args).ConfigureAwait(false);
                case "snapshot":
                    return args.Length > 1 && args[1] == "list"
                        ? CmdSnapshotsList(store)
                        : await CmdSnapshotAsync(args, store).ConfigureAwait(false);
                case "diagnose":
                    return await ComWindowsAsync(c => CmdDiagnoseAsync(args, c)).ConfigureAwait(false);
                case "repair":
                    return await ComWindowsAsync(c => CmdRepairAsync(args, c, store)).ConfigureAwait(false);
                default:
                    Console.Error.WriteLine($"Comando desconhecido: '{args[0]}'.");
                    Ajuda();
                    return (int)ExitCode.InvalidArguments;
            }
        }
        catch (PlatformNotSupportedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.InvalidArguments;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Erro: {ex.Message}");
            return (int)ExitCode.UnhandledError;
        }
    }

    // ---- Comandos -------------------------------------------------------------

    private static async Task<int> CmdListAsync(string[] args)
    {
        return await ComWindowsAsync(async ctx =>
        {
            var impressoras = await ctx.Gateway.ListPrintersAsync().ConfigureAwait(false);
            if (TemFlag(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(impressoras, JsonOpts));
            }
            else
            {
                foreach (var p in impressoras)
                {
                    Console.WriteLine($"{p.Name}  [{p.Protocol}] porta={p.PortName ?? "-"} driver={p.DriverName ?? "-"}");
                }

                Console.WriteLine($"{impressoras.Count} impressora(s).");
            }

            return (int)ExitCode.Ok;
        }).ConfigureAwait(false);
    }

    private static async Task<int> CmdSnapshotAsync(string[] args, SnapshotStore store)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Uso: snapshot <nome-impressora>");
            return (int)ExitCode.InvalidArguments;
        }

        return await ComWindowsAsync(async ctx =>
        {
            var alvo = await EncontrarAlvoAsync(ctx.Gateway, args[1]).ConfigureAwait(false);
            if (alvo is null)
            {
                Console.Error.WriteLine($"Impressora '{args[1]}' não encontrada.");
                return (int)ExitCode.InvalidArguments;
            }

            var capture = new CaptureWorkflow(ctx.Gateway);
            var snap = await capture.CaptureAsync(alvo, SnapshotOrigin.Manual).ConfigureAwait(false);
            await store.SaveAsync(snap).ConfigureAwait(false);
            Console.WriteLine($"Snapshot {snap.Id} gravado para '{alvo.Name}'.");
            return (int)ExitCode.Ok;
        }).ConfigureAwait(false);
    }

    private static int CmdSnapshotsList(SnapshotStore store)
    {
        var lista = store.ListAsync().GetAwaiter().GetResult();
        foreach (var s in lista)
        {
            Console.WriteLine($"{s.Id}  {s.CreatedAtUtc:yyyy-MM-dd HH:mm:ss}  {s.Origin}  {s.PrinterName}");
        }

        Console.WriteLine($"{lista.Count} snapshot(s).");
        return (int)ExitCode.Ok;
    }

    private static async Task<int> CmdDiagnoseAsync(string[] args, CompositionContext ctx)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Uso: diagnose <nome-impressora> [--json]");
            return (int)ExitCode.InvalidArguments;
        }

        var alvo = await EncontrarAlvoAsync(ctx.Gateway, args[1]).ConfigureAwait(false);
        if (alvo is null)
        {
            Console.Error.WriteLine($"Impressora '{args[1]}' não encontrada.");
            return (int)ExitCode.InvalidArguments;
        }

        var report = await ctx.Engine.DiagnoseAndPlanAsync(alvo).ConfigureAwait(false);
        if (TemFlag(args, "--json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOpts));
        }
        else
        {
            foreach (var check in report.Checks)
            {
                Console.WriteLine(OutputFormatter.Check(check));
            }

            Console.WriteLine();
            Console.WriteLine("Plano de reparo:");
            if (report.Plan.Steps.Count == 0)
            {
                Console.WriteLine("  (nada a fazer)");
            }
            else
            {
                foreach (var linha in OutputFormatter.Plano(report.Plan.Steps))
                {
                    Console.WriteLine($"  {linha}");
                }
            }
        }

        return (int)ExitCodeMapper.DeDiagnostico(report.Checks);
    }

    private static async Task<int> CmdRepairAsync(string[] args, CompositionContext ctx, SnapshotStore store)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Uso: repair <nome-impressora> [--yes]");
            return (int)ExitCode.InvalidArguments;
        }

        var alvo = await EncontrarAlvoAsync(ctx.Gateway, args[1]).ConfigureAwait(false);
        if (alvo is null)
        {
            Console.Error.WriteLine($"Impressora '{args[1]}' não encontrada.");
            return (int)ExitCode.InvalidArguments;
        }

        var report = await ctx.Engine.DiagnoseAndPlanAsync(alvo).ConfigureAwait(false);
        if (!TemFlag(args, "--yes"))
        {
            Console.WriteLine("Dry-run — plano que seria executado:");
            if (report.Plan.Steps.Count == 0)
            {
                Console.WriteLine("  (nada a fazer)");
            }
            else
            {
                foreach (var linha in OutputFormatter.Plano(report.Plan.Steps))
                {
                    Console.WriteLine($"  {linha}");
                }
            }

            Console.WriteLine("Execute novamente com --yes para aplicar.");
            return (int)ExitCode.Ok;
        }

        var lastGood = await store.FindLatestForAsync(alvo.Name).ConfigureAwait(false);
        if (lastGood is null && report.Plan.Steps.Any(s => s.Destructive))
        {
            Console.Error.WriteLine("Nenhum snapshot anterior — passos destrutivos serão pulados (SkippedNoSnapshot).");
        }

        var guard = new PolicyGuard();
        IRepairExecutor executor = OperatingSystem.IsWindows()
            ? new WindowsRepairExecutor()
            : throw new PlatformNotSupportedException("Execução de reparo requer Windows.");

        var workflow = new RepairWorkflow(executor, guard, store, new CaptureWorkflow(ctx.Gateway));
        var resultados = await workflow.RunAsync(report.Plan, lastGood).ConfigureAwait(false);

        foreach (var r in resultados)
        {
            Console.WriteLine($"[{r.Status}] {r.Kind}: {r.Detail}");
        }

        return (int)ExitCodeMapper.DeReparo(resultados);
    }

    // ---- Composição ------------------------------------------------------------

    private sealed record CompositionContext(IPrintSystemGateway Gateway, DiagnosticEngine Engine);

    private static async Task<int> ComWindowsAsync(Func<CompositionContext, Task<int>> corpo)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Este comando requer Windows (acesso ao subsistema de impressão).");
            return (int)ExitCode.InvalidArguments;
        }

        var gateway = new WinspoolPrintGateway();
        IDiagnosticCheck[] checks =
        [
            new SpoolerCheck(),
            new PortOpenCheck(),
            new DriverPresentCheck(),
            new QueueExistsCheck(),
            new QueueNotStuckCheck(),
            new NoDuplicateInstallCheck(),
        ];
        var engine = new DiagnosticEngine(gateway, checks, new RepairPlanner());
        return await corpo(new CompositionContext(gateway, engine)).ConfigureAwait(false);
    }

    private static async Task<PrinterTarget?> EncontrarAlvoAsync(IPrintSystemGateway gateway, string nome)
    {
        var impressoras = await gateway.ListPrintersAsync().ConfigureAwait(false);
        return impressoras.FirstOrDefault(p =>
            string.Equals(p.Name, nome, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TemFlag(string[] args, string flag)
        => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    internal static string CaminhoSnapshots()
    {
        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "PrinterRescue", "snapshots");
        }

        // Desenvolvimento em Linux/macOS: raiz local equivalente.
        return Path.Combine(
            Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
            "PrinterRescue", "snapshots");
    }

    private static void Ajuda()
    {
        Console.WriteLine("""
            Printer Rescue — restaura o estado funcional da sua impressora.

            Uso:
              printer-rescue list                          Lista impressoras instaladas
              printer-rescue snapshot <nome>               Grava snapshot do estado atual
              printer-rescue snapshot list                 Lista snapshots gravados
              printer-rescue diagnose <nome> [--json]      Diagnóstico determinístico + plano
              printer-rescue repair <nome> [--yes]         Dry-run; com --yes aplica o reparo

            REGRA Nº 1: este programa nunca baixa nem distribui drivers. Reinstala usando
            apenas o driver já presente na máquina ou o Microsoft IPP Class Driver.
            """);
    }
}
