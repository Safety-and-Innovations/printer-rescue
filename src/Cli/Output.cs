using PrinterRescue.Core;

namespace PrinterRescue.Cli;

/// <summary>Maps diagnostic/repair results to the contractual exit codes.</summary>
public static class ExitCodeMapper
{
    /// <summary>Priority: applied &gt; policy block &gt; missing snapshot &gt; failure &gt; ok.</summary>
    public static ExitCode FromRepair(IReadOnlyList<RepairOutcome> results)
    {
        if (results.Count == 0)
        {
            return ExitCode.Ok; // empty plan = dry-run with no steps
        }

        if (results.Any(r => r.Status == RepairStatus.Applied))
        {
            return ExitCode.RepairApplied;
        }

        if (results.Any(r => r.Status == RepairStatus.SkippedPolicyViolation))
        {
            return ExitCode.RepairBlockedByPolicy;
        }

        if (results.Any(r => r.Status == RepairStatus.SkippedNoSnapshot))
        {
            return ExitCode.SnapshotMissing;
        }

        return ExitCode.DiagnosticFailed;
    }

    public static ExitCode FromDiagnostics(IReadOnlyList<CheckOutcome> checks)
        => checks.Any(c => c.Result == CheckResult.Fail) ? ExitCode.DiagnosticFailed : ExitCode.Ok;
}

/// <summary>Text formatting for CLI output.</summary>
public static class OutputFormatter
{
    public static string Check(CheckOutcome c) => c.Result switch
    {
        CheckResult.Pass => $"[PASS] {c.Id}: {c.Detail}",
        CheckResult.Warn => $"[WARN] {c.Id}: {c.Detail}",
        CheckResult.Fail => $"[FAIL] {c.Id}: {c.Detail}",
        _ => $"[N/A ] {c.Id}: {c.Detail}",
    };

    public static IReadOnlyList<string> Plan(IReadOnlyList<RepairStep> steps)
    {
        var lines = new List<string>(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var markers = string.Concat(
                step.Destructive ? " [destructive]" : string.Empty,
                step.RequiresElevation ? " [elevation]" : string.Empty);
            lines.Add($"{i + 1}. {step.Kind}{markers} — {step.Description}");
        }

        return lines;
    }
}
