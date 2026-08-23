using PrinterRescue.Core;

namespace PrinterRescue.Cli;

/// <summary>Mapeia resultados de diagnóstico/reparo para os códigos de saída contratuais.</summary>
public static class ExitCodeMapper
{
    /// <summary>Prioridade: aplicado &gt; bloqueio por política &gt; falta de snapshot &gt; falha &gt; ok.</summary>
    public static ExitCode DeReparo(IReadOnlyList<RepairOutcome> resultados)
    {
        if (resultados.Count == 0)
        {
            return ExitCode.Ok; // plano vazio = dry-run sem passos
        }

        if (resultados.Any(r => r.Status == RepairStatus.Applied))
        {
            return ExitCode.RepairApplied;
        }

        if (resultados.Any(r => r.Status == RepairStatus.SkippedPolicyViolation))
        {
            return ExitCode.RepairBlockedByPolicy;
        }

        if (resultados.Any(r => r.Status == RepairStatus.SkippedNoSnapshot))
        {
            return ExitCode.SnapshotMissing;
        }

        return ExitCode.DiagnosticFailed;
    }

    public static ExitCode DeDiagnostico(IReadOnlyList<CheckOutcome> checks)
        => checks.Any(c => c.Result == CheckResult.Fail) ? ExitCode.DiagnosticFailed : ExitCode.Ok;
}

/// <summary>Formatação textual das saídas da CLI (pt-BR).</summary>
public static class OutputFormatter
{
    public static string Check(CheckOutcome c) => c.Result switch
    {
        CheckResult.Pass => $"[PASS] {c.Id}: {c.Detail}",
        CheckResult.Warn => $"[WARN] {c.Id}: {c.Detail}",
        CheckResult.Fail => $"[FAIL] {c.Id}: {c.Detail}",
        _ => $"[N/A ] {c.Id}: {c.Detail}",
    };

    public static IReadOnlyList<string> Plano(IReadOnlyList<RepairStep> passos)
    {
        var linhas = new List<string>(passos.Count);
        for (var i = 0; i < passos.Count; i++)
        {
            var p = passos[i];
            var marcadores = string.Concat(
                p.Destructive ? " [destrutivo]" : string.Empty,
                p.RequiresElevation ? " [elevação]" : string.Empty);
            linhas.Add($"{i + 1}. {p.Kind}{marcadores} — {p.Description}");
        }

        return linhas;
    }
}
