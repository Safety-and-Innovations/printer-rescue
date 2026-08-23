using System.Globalization;
using System.Text;
using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Policy;

/// <summary>
/// Guarda de conformidade do Printer Rescue — REGRA Nº 1:
/// nenhum passo pode baixar, hospedar, indexar ou distribuir driver de impressora.
/// O vetor de violação é a Description do passo: qualquer menção a obtenção ou
/// distribuição de driver nega o passo, independentemente do Kind. A comparação é
/// case-insensitive e ignora acentos ("catálogo" e "catalogo" são o mesmo termo).
/// </summary>
public sealed class PolicyGuard : IPolicyGuard
{
    /// <summary>Termos que indicam obtenção ou distribuição de driver — sempre proibidos.</summary>
    private static readonly string[] TermosProibidos =
    [
        "download",
        "baixar",
        "hospedar",
        "distribuir",
        "catálogo de driver",
        "instalar driver externo",
    ];

    /// <inheritdoc />
    public PolicyDecision Evaluate(RepairStep action, PrinterSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);

        var descricaoSemAcento = RemoverAcentos(action.Description);

        foreach (var termo in TermosProibidos)
        {
            if (descricaoSemAcento.Contains(RemoverAcentos(termo), StringComparison.OrdinalIgnoreCase))
            {
                return new PolicyDecision(
                    Allowed: false,
                    Reason: $"REGRA Nº 1: passo bloqueado — a descrição implica obtenção ou distribuição de driver (termo detectado: \"{termo}\").");
            }
        }

        return new PolicyDecision(
            Allowed: true,
            Reason: "Passo permitido: reparo local sobre o estado existente da máquina, sem obter ou distribuir driver.");
    }

    /// <summary>Normaliza o texto removendo diacríticos (NFD) para comparação tolerante.</summary>
    private static string RemoverAcentos(string texto)
    {
        var formaNormalizada = texto.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formaNormalizada.Length);
        foreach (var c in formaNormalizada)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                _ = builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
