namespace PrinterRescue.Core.Interfaces;

public sealed record PolicyDecision(bool Allowed, string Reason);

/// <summary>
/// Compliance guard (RULE #1): blocks any step that implies
/// downloading, hosting, indexing, or distributing printer drivers.
/// </summary>
public interface IPolicyGuard
{
    PolicyDecision Evaluate(global::PrinterRescue.Core.RepairStep action, global::PrinterRescue.Core.PrinterSnapshot context);
}
