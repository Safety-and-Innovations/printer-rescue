using System.Globalization;
using System.Text;
using PrinterRescue.Core.Interfaces;

namespace PrinterRescue.Core.Policy;

/// <summary>
/// Printer Rescue compliance guard — RULE #1:
/// no step may download, host, index, or distribute printer drivers.
/// The violation vector is the step Description: any mention of driver
/// acquisition or distribution denies the step, regardless of Kind. Comparison is
/// case-insensitive and accent-insensitive.
/// </summary>
public sealed class PolicyGuard : IPolicyGuard
{
    /// <summary>Terms indicating driver acquisition or distribution — always forbidden.</summary>
    private static readonly string[] ForbiddenTerms =
    [
        "download",
        "host",
        "distribute",
        "driver catalog",
        "install external driver",
    ];

    /// <inheritdoc />
    public PolicyDecision Evaluate(RepairStep action, PrinterSnapshot context)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(context);

        var normalizedDescription = RemoveDiacritics(action.Description);

        foreach (var term in ForbiddenTerms)
        {
            if (normalizedDescription.Contains(RemoveDiacritics(term), StringComparison.OrdinalIgnoreCase))
            {
                return new PolicyDecision(
                    Allowed: false,
                    Reason: $"RULE #1: step blocked — description implies driver acquisition or distribution (detected term: \"{term}\").");
            }
        }

        return new PolicyDecision(
            Allowed: true,
            Reason: "Step allowed: local repair over the machine's existing state, without acquiring or distributing drivers.");
    }

    /// <summary>Normalizes text by stripping diacritics (NFD) for tolerant comparison.</summary>
    private static string RemoveDiacritics(string text)
    {
        var normalizedForm = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalizedForm.Length);
        foreach (var c in normalizedForm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                _ = builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
