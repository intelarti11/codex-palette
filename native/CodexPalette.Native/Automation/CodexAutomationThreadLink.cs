using System.Globalization;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private readonly CodexAppServerThreadClient _threadClient = new();
    private readonly object _threadCatalogGate = new();
    private CodexCatalog? _threadCatalog;
    private IReadOnlyList<CodexCatalogEffort> _threadEfforts = Array.Empty<CodexCatalogEffort>();
    private IReadOnlyList<CodexCatalogServiceTier> _threadSpeedTiers = Array.Empty<CodexCatalogServiceTier>();

    public bool IsThreadLinked => _threadClient.IsLinked;
    public string? LinkedThreadLabel => _threadClient.LinkedThreadLabel;

    public Task<ThreadLinkResult> LinkActiveThreadAsync(CancellationToken cancellationToken = default) =>
        _threadClient.LinkAsync(cancellationToken);

    public void UnlinkActiveThread() => _threadClient.Unlink();

    private void ConfigureThreadCatalog(
        CodexCatalog catalog,
        IReadOnlyList<CodexCatalogEffort> effortOptions)
    {
        lock (_threadCatalogGate)
        {
            _threadCatalog = catalog;
            _threadEfforts = effortOptions.ToArray();
            _threadSpeedTiers = GetDesktopSpeedTiers(catalog);
        }
    }

    private void EnsureThreadCatalogMappings(CancellationToken cancellationToken)
    {
        lock (_threadCatalogGate)
        {
            if (_threadCatalog is not null && _threadEfforts.Count > 0)
            {
                return;
            }
        }

        _ = DiscoverPaletteCore(cancellationToken);
        lock (_threadCatalogGate)
        {
            if (_threadCatalog is null || _threadEfforts.Count == 0)
            {
                throw new AutomationUnavailableException(
                    "Le catalogue Codex ne contient pas les identifiants nécessaires pour modifier le fil lié.");
            }
        }
    }

    private (string ModelId, string EffortId) ResolveThreadSelectionIds(
        string modelName,
        string effortName,
        CancellationToken cancellationToken)
    {
        EnsureThreadCatalogMappings(cancellationToken);
        lock (_threadCatalogGate)
        {
            var model = _threadCatalog!.Models.FirstOrDefault(candidate =>
                string.Equals(
                    ToDesktopPickerModelName(candidate.DisplayName),
                    modelName,
                    StringComparison.Ordinal));
            if (model is null)
            {
                throw new AutomationUnavailableException(
                    $"Le modèle « {modelName} » n’existe plus dans le catalogue Codex.");
            }

            var effort = DesktopPickerEfforts(model).FirstOrDefault(candidate =>
                EffortLabelMatches(candidate.Id, effortName));
            if (effort is null)
            {
                throw new AutomationUnavailableException(
                    $"La puissance « {effortName} » n’est pas disponible pour « {modelName} ».");
            }

            return (model.Id, effort.Id);
        }
    }

    private string? ResolveThreadServiceTier(
        int speedIndex,
        CancellationToken cancellationToken)
    {
        EnsureThreadCatalogMappings(cancellationToken);
        lock (_threadCatalogGate)
        {
            if (speedIndex < 0 || speedIndex >= _threadSpeedTiers.Count)
            {
                throw new AutomationUnavailableException(
                    "Le niveau de vitesse demandé n’existe plus dans le catalogue Codex.");
            }

            var id = _threadSpeedTiers[speedIndex].Id;
            return string.Equals(id, "standard", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(id, "default", StringComparison.OrdinalIgnoreCase)
                ? null
                : id;
        }
    }

    private static IReadOnlyList<CodexCatalogServiceTier> GetDesktopSpeedTiers(CodexCatalog catalog)
    {
        var tiers = new List<CodexCatalogServiceTier>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tier in catalog.Models.SelectMany(static model => model.ServiceTiers))
        {
            if (ids.Add(tier.Id))
            {
                tiers.Add(tier);
            }
        }

        var standard = tiers.FirstOrDefault(tier =>
            string.Equals(tier.Id, "standard", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tier.Id, "default", StringComparison.OrdinalIgnoreCase));
        var alternate = tiers.FirstOrDefault(tier => standard is null ||
            !string.Equals(tier.Id, standard.Id, StringComparison.OrdinalIgnoreCase));
        return standard is null || alternate is null
            ? Array.Empty<CodexCatalogServiceTier>()
            : new[] { standard, alternate };
    }

    private static bool EffortLabelMatches(string effortId, string label)
    {
        var normalized = TextNormalizer.Normalize(label, effort: true);
        if (string.Equals(
                TextNormalizer.Normalize(ToDesktopPickerEffortName(effortId), effort: true),
                normalized,
                StringComparison.Ordinal) ||
            string.Equals(
                TextNormalizer.Normalize(CodexAppServerCatalogClient.EffortDisplayName(effortId), effort: true),
                normalized,
                StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(effortId, "medium", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var aliases = string.Equals(language, "fr", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Moyen", "Standard" }
            : new[] { "Medium", "Standard" };
        return aliases.Any(alias => string.Equals(
            TextNormalizer.Normalize(alias, effort: true),
            normalized,
            StringComparison.Ordinal));
    }

    private string WaitForPassiveSelection(
        int processId,
        string modelName,
        string effortName,
        CancellationToken cancellationToken)
    {
        var expectedModel = TextNormalizer.Normalize(modelName);
        var expectedEffort = TextNormalizer.Normalize(effortName, effort: true);
        var deadline = DateTime.UtcNow.AddMilliseconds(3500);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryReadCurrentSelection(processId, out var model, out var effort, out var selectorText) &&
                string.Equals(TextNormalizer.Normalize(model), expectedModel, StringComparison.Ordinal) &&
                string.Equals(
                    TextNormalizer.Normalize(effort, effort: true),
                    expectedEffort,
                    StringComparison.Ordinal))
            {
                return selectorText;
            }

            Thread.Sleep(50);
        }

        throw new AutomationUnavailableException(
            "Codex a accepté la modification du fil, mais son sélecteur visible ne s’est pas actualisé.");
    }
}
