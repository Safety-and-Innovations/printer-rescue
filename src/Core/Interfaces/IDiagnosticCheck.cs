namespace PrinterRescue.Core.Interfaces;

/// <summary>Individual deterministic diagnostic check.</summary>
public interface IDiagnosticCheck
{
    global::PrinterRescue.Core.CheckId Id { get; }

    Task<global::PrinterRescue.Core.CheckOutcome> RunAsync(
        IPrintSystemGateway gateway,
        global::PrinterRescue.Core.PrinterTarget target,
        CancellationToken ct = default);
}
