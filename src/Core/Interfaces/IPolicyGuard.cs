namespace PrinterRescue.Core.Interfaces;

public sealed record PolicyDecision(bool Allowed, string Reason);

/// <summary>
/// Guarda de conformidade (REGRA Nº 1): bloqueia qualquer passo que implique
/// baixar, hospedar, indexar ou distribuir driver de impressora.
/// </summary>
public interface IPolicyGuard
{
    PolicyDecision Evaluate(global::PrinterRescue.Core.RepairStep step, global::PrinterRescue.Core.PrinterSnapshot context);
}
