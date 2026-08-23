namespace PrinterRescue.Core.Interfaces;

/// <summary>Converte falhas de diagnóstico em plano de reparo ordenado.</summary>
public interface IRepairPlanner
{
    global::PrinterRescue.Core.RepairPlan PlanRepairs(
        global::PrinterRescue.Core.DiagnosticReport report,
        global::PrinterRescue.Core.PrinterSnapshot? lastGood);
}
