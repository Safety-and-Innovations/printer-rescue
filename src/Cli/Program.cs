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
/// Thin engine facade: object-graph composition and the
/// list / snapshot / diagnose / repair / snapshots commands.
/// Off Windows, only `snapshots list` works (local and cross-platform).
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
                PrintHelp();
                return (int)ExitCode.InvalidArguments;
            }

            var snapshotRoot = GetSnapshotRoot();
            var store = new SnapshotStore(snapshotRoot);

            switch (args[0].ToLowerInvariant())
            {
                case "list":
                    return await CmdListAsync(args).ConfigureAwait(false);
                case "snapshot":
                    return args.Length > 1 && args[1] == "list"
                        ? CmdSnapshotsList(store)
                        : await CmdSnapshotAsync(args, store).ConfigureAwait(false);
                case "diagnose":
                    return await WithWindowsAsync(c => CmdDiagnoseAsync(args, c)).ConfigureAwait(false);
                case "repair":
                    return await WithWindowsAsync(c => CmdRepairAsync(args, c, store)).ConfigureAwait(false);
                default:
                    Console.Error.WriteLine($"Unknown command: '{args[0]}'.");
                    PrintHelp();
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
            Console.Error.WriteLine($"Error: {ex.Message}");
            return (int)ExitCode.UnhandledError;
        }
    }

    // ---- Commands -------------------------------------------------------------

    private static async Task<int> CmdListAsync(string[] args)
    {
        return await WithWindowsAsync(async ctx =>
        {
            var printers = await ctx.Gateway.ListPrintersAsync().ConfigureAwait(false);
            if (HasFlag(args, "--json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(printers, JsonOpts));
            }
            else
            {
                foreach (var p in printers)
                {
                    Console.WriteLine($"{p.Name}  [{p.Protocol}] port={p.PortName ?? "-"} driver={p.DriverName ?? "-"}");
                }

                Console.WriteLine($"{printers.Count} printer(s).");
            }

            return (int)ExitCode.Ok;
        }).ConfigureAwait(false);
    }

    private static async Task<int> CmdSnapshotAsync(string[] args, SnapshotStore store)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: snapshot <printer-name>");
            return (int)ExitCode.InvalidArguments;
        }

        return await WithWindowsAsync(async ctx =>
        {
            var target = await FindTargetAsync(ctx.Gateway, args[1]).ConfigureAwait(false);
            if (target is null)
            {
                Console.Error.WriteLine($"Printer '{args[1]}' not found.");
                return (int)ExitCode.InvalidArguments;
            }

            var capture = new CaptureWorkflow(ctx.Gateway);
            var snap = await capture.CaptureAsync(target, SnapshotOrigin.Manual).ConfigureAwait(false);
            await store.SaveAsync(snap).ConfigureAwait(false);
            Console.WriteLine($"Snapshot {snap.Id} saved for '{target.Name}'.");
            return (int)ExitCode.Ok;
        }).ConfigureAwait(false);
    }

    private static int CmdSnapshotsList(SnapshotStore store)
    {
        var list = store.ListAsync().GetAwaiter().GetResult();
        foreach (var s in list)
        {
            Console.WriteLine($"{s.Id}  {s.CreatedAtUtc:yyyy-MM-dd HH:mm:ss}  {s.Origin}  {s.PrinterName}");
        }

        Console.WriteLine($"{list.Count} snapshot(s).");
        return (int)ExitCode.Ok;
    }

    private static async Task<int> CmdDiagnoseAsync(string[] args, CompositionContext ctx)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: diagnose <printer-name> [--json]");
            return (int)ExitCode.InvalidArguments;
        }

        var target = await FindTargetAsync(ctx.Gateway, args[1]).ConfigureAwait(false);
        if (target is null)
        {
            Console.Error.WriteLine($"Printer '{args[1]}' not found.");
            return (int)ExitCode.InvalidArguments;
        }

        var report = await ctx.Engine.DiagnoseAndPlanAsync(target).ConfigureAwait(false);
        if (HasFlag(args, "--json"))
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
            Console.WriteLine("Repair plan:");
            if (report.Plan.Steps.Count == 0)
            {
                Console.WriteLine("  (nothing to do)");
            }
            else
            {
                foreach (var line in OutputFormatter.Plan(report.Plan.Steps))
                {
                    Console.WriteLine($"  {line}");
                }
            }
        }

        return (int)ExitCodeMapper.FromDiagnostics(report.Checks);
    }

    private static async Task<int> CmdRepairAsync(string[] args, CompositionContext ctx, SnapshotStore store)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: repair <printer-name> [--yes]");
            return (int)ExitCode.InvalidArguments;
        }

        var target = await FindTargetAsync(ctx.Gateway, args[1]).ConfigureAwait(false);
        if (target is null)
        {
            Console.Error.WriteLine($"Printer '{args[1]}' not found.");
            return (int)ExitCode.InvalidArguments;
        }

        var report = await ctx.Engine.DiagnoseAndPlanAsync(target).ConfigureAwait(false);
        if (!HasFlag(args, "--yes"))
        {
            Console.WriteLine("Dry-run — plan that would run:");
            if (report.Plan.Steps.Count == 0)
            {
                Console.WriteLine("  (nothing to do)");
            }
            else
            {
                foreach (var line in OutputFormatter.Plan(report.Plan.Steps))
                {
                    Console.WriteLine($"  {line}");
                }
            }

            Console.WriteLine("Run again with --yes to apply.");
            return (int)ExitCode.Ok;
        }

        var lastGood = await store.FindLatestForAsync(target.Name).ConfigureAwait(false);
        if (lastGood is null && report.Plan.Steps.Any(s => s.Destructive))
        {
            Console.Error.WriteLine("No prior snapshot — destructive steps will be skipped (SkippedNoSnapshot).");
        }

        var guard = new PolicyGuard();
        IRepairExecutor executor = OperatingSystem.IsWindows()
            ? new WindowsRepairExecutor()
            : throw new PlatformNotSupportedException("Repair execution requires Windows.");

        var workflow = new RepairWorkflow(executor, guard, store, new CaptureWorkflow(ctx.Gateway));
        var results = await workflow.RunAsync(report.Plan, lastGood).ConfigureAwait(false);

        foreach (var r in results)
        {
            Console.WriteLine($"[{r.Status}] {r.Kind}: {r.Detail}");
        }

        return (int)ExitCodeMapper.FromRepair(results);
    }

    // ---- Composition ------------------------------------------------------------

    private sealed record CompositionContext(IPrintSystemGateway Gateway, DiagnosticEngine Engine);

    private static async Task<int> WithWindowsAsync(Func<CompositionContext, Task<int>> body)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("This command requires Windows (print subsystem access).");
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
        return await body(new CompositionContext(gateway, engine)).ConfigureAwait(false);
    }

    private static async Task<PrinterTarget?> FindTargetAsync(IPrintSystemGateway gateway, string name)
    {
        var printers = await gateway.ListPrintersAsync().ConfigureAwait(false);
        return printers.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasFlag(string[] args, string flag)
        => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    internal static string GetSnapshotRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "PrinterRescue", "snapshots");
        }

        // Development on Linux/macOS: equivalent local root.
        return Path.Combine(
            Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
            "PrinterRescue", "snapshots");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Printer Rescue — restores your printer to working order.

            Usage:
              printer-rescue list                          List installed printers
              printer-rescue snapshot <name>               Save a snapshot of the current state
              printer-rescue snapshot list                 List saved snapshots
              printer-rescue diagnose <name> [--json]      Deterministic diagnostics + plan
              printer-rescue repair <name> [--yes]         Dry-run; with --yes applies the repair

            RULE #1: this program never downloads or distributes drivers. It reinstalls using
            only the driver already present on the machine or the Microsoft IPP Class Driver.
            """);
    }
}
