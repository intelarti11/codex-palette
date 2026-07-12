using System.Globalization;
using System.Text.RegularExpressions;
using CodexPalette.Native.Models;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private NativePaletteState DiscoverPaletteCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = TryFindCodexProcess() ??
            throw new AutomationUnavailableException("The official Codex window could not be found.");

        // Catalog discovery is deliberately independent from the visible selector. It does not
        // expand the main menu, visit models, change the current selection, or move focus.
        var catalog = _catalogClient.Load(process, cancellationToken);
        if (catalog.Models.Count == 0)
        {
            throw new AutomationUnavailableException("Codex returned an empty model catalog.");
        }

        var effortOptions = new List<CodexCatalogEffort>();
        var effortIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var effort in catalog.Models.SelectMany(DesktopPickerEfforts))
        {
            if (effortIds.ContainsKey(effort.Id))
            {
                continue;
            }

            effortIds.Add(effort.Id, effortOptions.Count);
            effortOptions.Add(effort);
        }

        var models = catalog.Models
            .Select(static model => ToDesktopPickerModelName(model.DisplayName))
            .ToArray();
        var efforts = effortOptions
            .Select(static effort => ToDesktopPickerEffortName(effort.Id))
            .ToArray();
        var supported = catalog.Models
            .Select(model => (IReadOnlyList<int>)DesktopPickerEfforts(model)
                .Select(effort => effortIds.TryGetValue(effort.Id, out var index) ? index : -1)
                .Where(static index => index >= 0)
                .Distinct()
                .Order()
                .ToArray())
            .ToArray();

        ConfigureThreadCatalog(catalog, effortOptions);
        UpdateCachedMatrix(models, efforts, supported);
        UpdateSpeedFromCatalog(catalog);

        return ReadStateCore(cancellationToken);
    }

    private static IEnumerable<CodexCatalogEffort> DesktopPickerEfforts(CodexCatalogModel model)
    {
        var hasUltra = model.Efforts.Any(static effort =>
            string.Equals(effort.Id, "ultra", StringComparison.OrdinalIgnoreCase));
        return model.Efforts.Where(effort =>
            !(hasUltra && string.Equals(effort.Id, "max", StringComparison.OrdinalIgnoreCase)));
    }

    private static string ToDesktopPickerModelName(string displayName)
    {
        var value = TextNormalizer.Normalize(displayName);
        if (value.StartsWith("GPT-", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..];
        }

        value = Regex.Replace(value, @"(?<=\d)-(?=[A-Za-z])", " ", RegexOptions.CultureInvariant);
        value = value.Replace('-', ' ');
        return TextNormalizer.Normalize(value);
    }

    private static string ToDesktopPickerEffortName(string id)
    {
        var french = string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            "fr",
            StringComparison.OrdinalIgnoreCase);
        return id.ToLowerInvariant() switch
        {
            "none" => french ? "Aucun" : "None",
            "minimal" => "Minimal",
            "low" => french ? "Léger" : "Low",
            "medium" => french ? "Standard" : "Medium",
            "high" => french ? "Élevé" : "High",
            "xhigh" => french ? "Très élevé" : "Extra high",
            "max" => french ? "Maximum" : "Max",
            "ultra" => "Ultra",
            _ => TextNormalizer.Normalize(id.Replace('_', ' ').Replace('-', ' ')),
        };
    }

    private void UpdateSpeedFromCatalog(CodexCatalog catalog)
    {
        var tiers = GetDesktopSpeedTiers(catalog);
        if (tiers.Count != 2)
        {
            return;
        }

        var speedLabel = string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            "fr",
            StringComparison.OrdinalIgnoreCase)
            ? "Vitesse"
            : "Speed";
        UpdateCachedSpeed(speedLabel, new[] { tiers[0].DisplayName, tiers[1].DisplayName });
    }
}
