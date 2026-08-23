using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Repair;

/// <summary>
/// Converte falhas de diagnóstico em plano de reparo ordenado e conservador.
/// Contrato: ClearQueue &lt; RestartSpooler &lt; RestorePort &lt; Reinstall* &lt; RemoveBrokenInstall.
/// Passo destrutivo só entra no plano quando existe snapshot válido (lastGood) para reversão;
/// sem snapshot, os candidatos destrutivos são marcados SkippedNoSnapshot na descrição do plano.
/// As descrições geradas nunca mencionam obtenção ou distribuição de driver (REGRA Nº 1).
/// </summary>
public sealed class RepairPlanner : IRepairPlanner
{
    /// <summary>Ordem global contratual de execução dos passos.</summary>
    private static readonly RepairActionKind[] OrdemGlobal =
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

        var candidatos = new List<RepairStep>();
        var pendencias = new List<string>();

        if (Resultado(report, CheckId.QueueNotStuck) is CheckResult.Warn or CheckResult.Fail)
        {
            candidatos.Add(new RepairStep(
                RepairActionKind.ClearQueue,
                "Limpar a fila de impressão removendo os trabalhos presos.",
                Destructive: false,
                RequiresElevation: false));
            candidatos.Add(new RepairStep(
                RepairActionKind.RestartSpooler,
                "Reiniciar o serviço Print Spooler do Windows.",
                Destructive: false,
                RequiresElevation: true));
        }

        if (Resultado(report, CheckId.PortOpen) == CheckResult.Fail)
        {
            if (lastGood?.Port is { } portaBoa)
            {
                candidatos.Add(new RepairStep(
                    RepairActionKind.RestorePort,
                    $"Restaurar a porta '{portaBoa.PortName}' ({portaBoa.HostAddress}:{portaBoa.PortNumber}, protocolo {portaBoa.Protocol}) conforme o snapshot válido.",
                    Destructive: false,
                    RequiresElevation: true));
            }
            else
            {
                pendencias.Add("[SkippedNoSnapshot] RestorePort não planejado: restauração de porta exige snapshot válido.");
            }
        }

        if (Resultado(report, CheckId.DriverPresent) == CheckResult.Fail)
        {
            if (lastGood is not null)
            {
                candidatos.Add(new RepairStep(
                    RepairActionKind.ReinstallWithIppClassDriver,
                    "Reinstalar a impressora usando o driver de classe IPP nativo do sistema operacional.",
                    Destructive: true,
                    RequiresElevation: true));
                candidatos.Add(new RepairStep(
                    RepairActionKind.ReinstallFromDriverStore,
                    "Reinstalar a impressora a partir do Driver Store local da máquina, sem acesso à rede externa.",
                    Destructive: true,
                    RequiresElevation: true));
                candidatos.Add(new RepairStep(
                    RepairActionKind.RemoveBrokenInstall,
                    "Remover do sistema a instalação danificada da impressora, preservando o snapshot para reversão.",
                    Destructive: true,
                    RequiresElevation: true));
            }
            else
            {
                pendencias.Add("[SkippedNoSnapshot] ReinstallWithIppClassDriver e ReinstallFromDriverStore não planejados: reinstalação destrutiva exige snapshot válido.");
                pendencias.Add("[SkippedNoSnapshot] RemoveBrokenInstall suprimido: remoção destrutiva exige snapshot válido.");
            }
        }

        var ordenados = candidatos
            .OrderBy(static s => Array.IndexOf(OrdemGlobal, s.Kind))
            .ToList();

        if (pendencias.Count > 0)
        {
            var nota = $" {string.Join(" ", pendencias)}";
            if (ordenados.Count > 0)
            {
                var ultimo = ordenados[^1];
                ordenados[^1] = ultimo with { Description = $"{ultimo.Description}{nota}" };
            }
            else
            {
                // Plano sem passos executáveis: registra a pendência como entrada informativa.
                ordenados.Add(new RepairStep(
                    RepairActionKind.RestoreDefaults,
                    $"Nenhum passo executável neste plano.{nota}",
                    Destructive: false,
                    RequiresElevation: false));
            }
        }

        return new RepairPlan(report.TargetId, ordenados, ordenados.Any(static s => s.RequiresElevation));
    }

    private static CheckResult Resultado(DiagnosticReport report, CheckId id)
        => report.Checks.FirstOrDefault(c => c.Id == id)?.Result ?? CheckResult.NotApplicable;
}
