using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Repair;

/// <summary>
/// Converts diagnostic failures into an ordered, conservative repair plan.
/// Contract: ClearQueue &lt; RestartSpooler &lt; RestorePort &lt; Reinstall* &lt; RemoveBrokenInstall.
/// A destructive step only enters the plan when a valid snapshot (lastGood) exists for rollback;
/// without a snapshot, destructive candidates are flagged SkippedNoSnapshot in the plan description.
/// Generated descriptions never mention driver acquisition or distribution (RULE #1).
/// </summary>
public sealed class RepairPlanner : IRepairPlanner
{
    /// <summary>Contractual global execution order of steps.</summary>
    private static readonly RepairActionKind[] GlobalOrder =
    [
        RepairActionKind.ClearQueue,
        RepairActionKind.RestartSpooler,
        RepairActionKind.RestorePort,
        RepairActionKind.RestorePermissions,
        RepairActionKind.RestoreDefaults,
        RepairActionKind.ReinstallWithIppClassDriver,
        RepairActionKind.ReinstallFromDriverStore,
        RepairActionKind.RemoveBrokenInstall,
    ];

    /// <inheritdoc />
    public RepairPlan PlanRepairs(DiagnosticReport report, PrinterSnapshot? lastGood)
    {
        ArgumentNullException.ThrowIfNull(report);

        var candidates = new List<RepairStep>();
        var pendingNotes = new List<string>();

        if (GetResult(report, CheckId.QueueNotStuck) is CheckResult.Warn or CheckResult.Fail)
        {
            candidates.Add(new RepairStep(
                RepairActionKind.ClearQueue,
                "Clear the print queue by removing the stuck jobs.",
                Destructive: false,
                RequiresElevation: false));
            candidates.Add(new RepairStep(
                RepairActionKind.RestartSpooler,
                "Restart the Windows Print Spooler service.",
                Destructive: false,
                RequiresElevation: true));
        }

        if (GetResult(report, CheckId.PortOpen) == CheckResult.Fail)
        {
            if (lastGood?.Port is { } goodPort)
            {
                candidates.Add(new RepairStep(
                    RepairActionKind.RestorePort,
                    $"Restore port '{goodPort.PortName}' ({goodPort.HostAddress}:{goodPort.PortNumber}, protocol {goodPort.Protocol}) from the valid snapshot.",
                    Destructive: false,
                    RequiresElevation: true));
            }
            else
            {
                pendingNotes.Add("[SkippedNoSnapshot] RestorePort not planned: port restore requires a valid snapshot.");
            }
        }

        if (GetResult(report, CheckId.DriverPresent) == CheckResult.Fail)
        {
            if (lastGood is not null)
            {
                candidates.Add(new RepairStep(
                    RepairActionKind.ReinstallWithIppClassDriver,
                    "Reinstall the printer using the operating system's native IPP class driver.",
                    Destructive: true,
                    RequiresElevation: true));
                candidates.Add(new RepairStep(
                    RepairActionKind.ReinstallFromDriverStore,
                    "Reinstall the printer from the machine's local driver store, with no external network access.",
                    Destructive: true,
                    RequiresElevation: true));
                candidates.Add(new RepairStep(
                    RepairActionKind.RemoveBrokenInstall,
                    "Remove the printer's broken installation from the system, keeping the snapshot for rollback.",
                    Destructive: true,
                    RequiresElevation: true));
            }
            else
            {
                pendingNotes.Add("[SkippedNoSnapshot] ReinstallWithIppClassDriver and ReinstallFromDriverStore not planned: destructive reinstall requires a valid snapshot.");
                pendingNotes.Add("[SkippedNoSnapshot] RemoveBrokenInstall suppressed: destructive removal requires a valid snapshot.");
            }
        }

        var ordered = candidates
            .OrderBy(static s => Array.IndexOf(GlobalOrder, s.Kind))
            .ToList();

        if (pendingNotes.Count > 0)
        {
            var note = $" {string.Join(" ", pendingNotes)}";
            if (ordered.Count > 0)
            {
                var last = ordered[^1];
                ordered[^1] = last with { Description = $"{last.Description}{note}" };
            }
            else
            {
                // Plan with no runnable steps: record the pending note as an informational entry.
                ordered.Add(new RepairStep(
                    RepairActionKind.RestoreDefaults,
                    $"No runnable steps in this plan.{note}",
                    Destructive: false,
                    RequiresElevation: false));
            }
        }

        return new RepairPlan(report.TargetId, ordered, ordered.Any(static s => s.RequiresElevation));
    }

    private static CheckResult GetResult(DiagnosticReport report, CheckId id)
        => report.Checks.FirstOrDefault(c => c.Id == id)?.Result ?? CheckResult.NotApplicable;
}
