using System.Text.RegularExpressions;

namespace CodexPalette.Native.Automation;

public static partial class TextNormalizer
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static string Normalize(string? value) => Normalize(value, effort: false);

    public static string Normalize(string? value, bool effort)
    {
        var normalized = WhitespaceRegex().Replace(value ?? string.Empty, " ").Trim();
        if (effort && normalized.StartsWith("Ultra", StringComparison.OrdinalIgnoreCase))
        {
            return "Ultra";
        }

        return normalized;
    }

    public static string GetGroupLabel(string ownerName, IReadOnlyList<string> descendantTexts, IReadOnlyList<string> labels)
    {
        var firstIndependentText = descendantTexts.FirstOrDefault(text => !labels.Contains(text, StringComparer.Ordinal));
        if (!string.IsNullOrWhiteSpace(firstIndependentText))
        {
            return firstIndependentText;
        }

        var normalizedName = Normalize(ownerName);
        foreach (var label in labels.OrderByDescending(static label => label.Length))
        {
            if (!normalizedName.EndsWith(label, StringComparison.Ordinal))
            {
                continue;
            }

            var prefix = normalizedName[..^label.Length].Trim(' ', ':', '-', '·');
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                return prefix;
            }
        }

        return string.Empty;
    }
}
