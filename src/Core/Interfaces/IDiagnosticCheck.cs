namespace PrinterRescue.Core.Interfaces;

/// <summary>Verificação determinística individual do diagnóstico.</summary>
public interface IDiagnosticCheck
{
    global::PrinterRescue.Core.CheckId Id { get; }

    Task<global::PrinterRescue.Core.CheckOutcome> RunAsync(
        IPrintSystemGateway gateway,
        global::PrinterRescue.Core.PrinterTarget target,
        CancellationToken ct = default);
}
