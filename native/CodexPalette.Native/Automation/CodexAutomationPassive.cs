using System.Diagnostics;
using System.Windows.Automation;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService : IDisposable
{
    private readonly object _cacheGate = new();
    private readonly DiscoveryCache _cacheStore = new();
    private DiscoveryCacheData _cache;
    private readonly AutomationEventHandler _menuOpenedHandler;
    private bool _disposed;

    public CodexAutomationService()
    {
        _cache = _cacheStore.Load();
        _menuOpenedHandler = MenuOpened;
        try
        {
            Automation.AddAutomationEventHandler(
                AutomationElement.MenuOpenedEvent,
                AutomationElement.RootElement,
                TreeScope.Subtree,
                _menuOpenedHandler);
        }
        catch
        {
            // Passive observation is optional; direct reads still work.
        }
    }

    public event EventHandler? PassiveStateChanged;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Automation.RemoveAutomationEventHandler(
                AutomationElement.MenuOpenedEvent,
                AutomationElement.RootElement,
                _menuOpenedHandler);
        }
        catch
        {
            // UI Automation may already be shutting down.
        }
    }

    private DiscoveryCacheData GetCachedDiscovery()
    {
        lock (_cacheGate)
        {
            return new DiscoveryCacheData(
                _cache.Efforts.ToArray(),
                _cache.SpeedLabel,
                _cache.Speeds.ToArray());
        }
    }

    private void UpdateCachedEfforts(IReadOnlyList<string> efforts)
    {
        var normalized = efforts
            .Select(static label => TextNormalizer.Normalize(label, effort: true))
            .Where(static label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToArray();
        if (normalized.Length is < 4 or > 5)
        {
            return;
        }

        UpdateCache(current => current with { Efforts = normalized });
    }

    private void UpdateCachedSpeed(string label, IReadOnlyList<string> speeds)
    {
        var normalized = speeds
            .Select(TextNormalizer.Normalize)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (normalized.Length != 2)
        {
            return;
        }

        UpdateCache(current => current with
        {
            SpeedLabel = TextNormalizer.Normalize(label),
            Speeds = normalized,
        });
    }

    private void UpdateCache(Func<DiscoveryCacheData, DiscoveryCacheData> update)
    {
        DiscoveryCacheData next;
        lock (_cacheGate)
        {
            next = update(_cache);
            if (next == _cache ||
                (next.SpeedLabel == _cache.SpeedLabel &&
                 next.Efforts.SequenceEqual(_cache.Efforts, StringComparer.Ordinal) &&
                 next.Speeds.SequenceEqual(_cache.Speeds, StringComparer.Ordinal)))
            {
                return;
            }

            _cache = next;
        }

        _cacheStore.Save(next);
        PassiveStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MenuOpened(object sender, AutomationEventArgs args)
    {
        if (_disposed || sender is not AutomationElement menu)
        {
            return;
        }

        int processId;
        try
        {
            processId = menu.Current.ProcessId;
        }
        catch
        {
            return;
        }

        if (!IsCodexProcess(processId))
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => ObserveOpenedMenu(menu, processId));
    }

    private void ObserveOpenedMenu(AutomationElement menu, int processId)
    {
        try
        {
            var items = GetDescendants(menu)
                .Where(IsOptionElement)
                .Where(TestVisible)
                .DistinctBy(GetRuntimeKey)
                .ToArray();
            var labels = items
                .Select(item => GetLabel(item, effort: false))
                .Where(static label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (labels.Length is 4 or 5)
            {
                using var process = TryFindCodexProcess();
                if (process?.Id == processId && TryReadCurrentSelection(processId, out _, out var effort))
                {
                    var normalizedEffort = TextNormalizer.Normalize(effort, effort: true);
                    if (labels.Any(label =>
                            string.Equals(
                                TextNormalizer.Normalize(label, effort: true),
                                normalizedEffort,
                                StringComparison.Ordinal)))
                    {
                        UpdateCachedEfforts(labels);
                    }
                }
            }
            else if (labels.Length == 2)
            {
                var trigger = FindTriggerForLabels(processId, labels);
                if (trigger is not null)
                {
                    var groupLabel = TextNormalizer.GetGroupLabel(
                        SafeName(trigger),
                        GetTexts(trigger),
                        labels);
                    UpdateCachedSpeed(groupLabel, labels);
                }
            }
        }
        catch
        {
            // Passive learning must never interfere with Codex.
        }
    }

    private static AutomationElement? FindTriggerForLabels(int processId, IReadOnlyList<string> labels) =>
        GetElements(processId)
            .Where(IsPopupTrigger)
            .FirstOrDefault(element =>
            {
                var values = GetElementStrings(element);
                return labels.Any(label => values.Any(value =>
                    value.EndsWith(label, StringComparison.Ordinal) ||
                    value.Contains(label, StringComparison.Ordinal)));
            });

    private static bool IsCodexProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!string.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var title = process.MainWindowTitle ?? string.Empty;
            if (title.Contains("Codex", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var path = process.MainModule?.FileName ?? string.Empty;
            return path.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadCurrentSelection(int processId, out string model, out string effort)
    {
        model = string.Empty;
        effort = string.Empty;
        try
        {
            var selector = FindElement(
                processId,
                ControlType.Button,
                SelectorRegex().ToString(),
                exact: false,
                timeoutMilliseconds: 400,
                CancellationToken.None);
            var selectorName = TextNormalizer.Normalize(selector.Current.Name);
            model = ModelNames.FirstOrDefault(name =>
                selectorName.StartsWith(name + " ", StringComparison.Ordinal)) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(model))
            {
                return false;
            }

            effort = selectorName[model.Length..].Trim();
            return !string.IsNullOrWhiteSpace(effort);
        }
        catch
        {
            return false;
        }
    }

    private void LearnFromPassiveTree(int processId, string currentEffort)
    {
        try
        {
            var groups = new Dictionary<string, PassiveOptionGroup>(StringComparer.Ordinal);
            foreach (var item in GetElements(processId).Where(IsOptionElement))
            {
                if (!TryGetPattern(item, SelectionItemPattern.Pattern, out var selectionValue))
                {
                    continue;
                }

                AutomationElement? container;
                try
                {
                    container = ((SelectionItemPattern)selectionValue).Current.SelectionContainer;
                }
                catch
                {
                    continue;
                }

                if (container is null)
                {
                    continue;
                }

                var key = GetRuntimeKey(container);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new PassiveOptionGroup(container, []);
                    groups.Add(key, group);
                }

                group.Items.Add(item);
            }

            foreach (var group in groups.Values)
            {
                var labels = group.Items
                    .Select(item => GetLabel(item, effort: false))
                    .Where(static label => !string.IsNullOrWhiteSpace(label))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                if (labels.Length is 4 or 5 && labels.Any(label =>
                        string.Equals(
                            TextNormalizer.Normalize(label, effort: true),
                            TextNormalizer.Normalize(currentEffort, effort: true),
                            StringComparison.Ordinal)))
                {
                    UpdateCachedEfforts(labels);
                }
                else if (labels.Length == 2)
                {
                    var trigger = FindTriggerForLabels(processId, labels);
                    if (trigger is not null)
                    {
                        UpdateCachedSpeed(
                            TextNormalizer.GetGroupLabel(SafeName(trigger), GetTexts(trigger), labels),
                            labels);
                    }
                }
            }
        }
        catch
        {
            // Closed Chromium popups may not exist in the automation tree.
        }
    }

    private sealed record PassiveOptionGroup(AutomationElement Container, List<AutomationElement> Items);
}
