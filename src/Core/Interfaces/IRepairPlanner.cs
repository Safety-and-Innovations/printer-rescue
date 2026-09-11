namespace PrinterRescue.Core.Interfaces;

/// <summary>Converts diagnostic failures into an ordered repair plan.</summary>
public interface IRepairPlanner
{
    global::PrinterRescue.Core.RepairPlan PlanRepairs(
        global::PrinterRescue.Core.DiagnosticReport report,
        global::PrinterRescue.Core.PrinterSnapshot? lastGood);
}
