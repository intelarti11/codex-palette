using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private AutomationContext GetContext(int processId, CancellationToken cancellationToken)
    {
        var selector = FindSelector(processId, 2500, cancellationToken);
        var selectorName = TextNormalizer.Normalize(selector.Current.Name);
        var visibleBefore = GetVisibleRuntimeKeys(processId);
        OpenSilent(selector, cancellationToken);

        var popup = FindSelectorPopup(processId, selector, visibleBefore, cancellationToken);
        var advancedToggle = FindAdvancedToggle(popup);
        var advancedWasExpanded = advancedToggle is not null &&
                                  GetExpandCollapseState(advancedToggle) == ExpandCollapseState.Expanded;
        var restoreNormalLayout = false;

        if (advancedToggle is not null && !advancedWasExpanded)
        {
            OpenSilent(advancedToggle, cancellationToken);
            restoreNormalLayout = true;
            Thread.Sleep(120);
            popup = TryRefreshSelectorPopup(processId, selector, popup, cancellationToken);
        }

        var directSources = GetDirectOptionSources(popup);
        var expandableControls = WaitForSelectorControls(
            popup,
            selector,
            advancedToggle,
            cancellationToken);
        var sources = new List<OptionSource>(directSources);

        foreach (var control in expandableControls)
        {
            try
            {
                var options = GetMenuOptions(processId, control, 1, 20, false, cancellationToken);
                CloseSilent(control);
                sources.Add(new OptionSource(control, options, RequiresOpen: true));
            }
            catch
            {
                CloseSilent(control);
            }
        }

        OptionSource? modelControl = null;
        OptionSource? effortControl = null;
        OptionSource? speedControl = null;

        foreach (var source in sources)
        {
            var options = source.Snapshot;
            if (modelControl is null && LooksLikeModelOptions(options.Labels, selectorName))
            {
                modelControl = source;
                continue;
            }

            var effortOptions = NormalizeMenuOptions(options, effort: true);
            var matchesCurrentSuffix = effortOptions.Labels.Any(label =>
                selectorName.EndsWith(" " + label, StringComparison.Ordinal));
            if (effortControl is null && matchesCurrentSuffix && effortOptions.Labels.Count >= 2)
            {
                effortControl = source with { Snapshot = effortOptions };
                continue;
            }

            if (speedControl is null && options.Labels.Count == 2)
            {
                speedControl = source;
            }
        }

        var speedToggle = FindSpeedToggle(popup);
        if (modelControl is null || effortControl is null)
        {
            CloseSelectorLayout(selector, advancedToggle, restoreNormalLayout, expandableControls);
            throw new AutomationUnavailableException(
                "The Codex selector opened, but its model and reasoning controls could not be classified " +
                "in either the normal or advanced layout.");
        }

        var model = modelControl.Snapshot.Labels
            .OrderByDescending(static value => value.Length)
            .FirstOrDefault(value =>
                string.Equals(selectorName, value, StringComparison.Ordinal) ||
                selectorName.StartsWith(value + " ", StringComparison.Ordinal)) ??
            throw new AutomationUnavailableException("The current model could not be identified from native options.");
        var effort = selectorName[model.Length..].Trim();

        return new AutomationContext(
            selector,
            popup,
            advancedWasExpanded ? SelectorLayout.Advanced : SelectorLayout.Normal,
            advancedToggle,
            restoreNormalLayout,
            model,
            effort,
            expandableControls,
            modelControl.Control,
            effortControl.Control,
            speedControl?.Control ?? speedToggle,
            speedToggle,
            modelControl.Snapshot,
            effortControl.Snapshot);
    }

    private static AutomationElement FindSelectorPopup(
        int processId,
        AutomationElement selector,
        IReadOnlySet<string> visibleBefore,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(1100);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = GetElements(processId)
                .Where(TestVisible)
                .Where(IsPopupContainer)
                .Where(element => !SameElement(element, selector))
                .Where(element => IsNearSelectorPopup(element, selector))
                .Select(element => new
                {
                    Element = element,
                    Score = ScoreSelectorPopup(element, selector, visibleBefore),
                })
                .OrderBy(static candidate => candidate.Score)
                .ToArray();

            if (candidates.Length > 0)
            {
                return candidates[0].Element;
            }

            Thread.Sleep(30);
        }

        CloseSilent(selector);
        throw new AutomationUnavailableException("The Codex selector popup was not exposed through UI Automation.");
    }

    private static AutomationElement TryRefreshSelectorPopup(
        int processId,
        AutomationElement selector,
        AutomationElement fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            return FindSelectorPopup(
                processId,
                selector,
                GetVisibleRuntimeKeys(processId),
                cancellationToken);
        }
        catch
        {
            return fallback;
        }
    }

    private static double ScoreSelectorPopup(
        AutomationElement element,
        AutomationElement selector,
        IReadOnlySet<string> visibleBefore)
    {
        var runtimeKey = GetRuntimeKey(element);
        var existingPenalty = runtimeKey is not null && visibleBefore.Contains(runtimeKey) ? 1_000_000 : 0;
        var typePenalty = GetControlType(element) == ControlType.Menu ? 0 : 150_000;
        var selectionPenalty = TryGetPattern(element, SelectionPattern.Pattern, out _) ? 0 : 25_000;
        var namePenalty = string.Equals(
            TextNormalizer.Normalize(SafeName(element)),
            TextNormalizer.Normalize(SafeName(selector)),
            StringComparison.Ordinal) ? 0 : 5_000;
        var bounds = SafeBounds(element);
        var anchor = SafeBounds(selector);
        if (bounds.IsEmpty || anchor.IsEmpty)
        {
            return existingPenalty + typePenalty + selectionPenalty + namePenalty + 900_000;
        }

        var dx = bounds.X + bounds.Width / 2 - (anchor.X + anchor.Width / 2);
        var dy = bounds.Y + bounds.Height / 2 - (anchor.Y + anchor.Height / 2);
        return existingPenalty + typePenalty + selectionPenalty + namePenalty +
               Math.Sqrt(dx * dx + dy * dy) * 100 + Math.Min(bounds.Width * bounds.Height, 500_000);
    }

    private static AutomationElement? FindAdvancedToggle(AutomationElement popup)
    {
        var directTriggers = GetChildren(popup)
            .Where(TestVisible)
            .Where(IsPopupTrigger)
            .ToArray();
        if (directTriggers.Length == 0)
        {
            return null;
        }

        if (directTriggers.Length == 1)
        {
            return directTriggers[0];
        }

        var expanded = directTriggers
            .Where(element => GetExpandCollapseState(element) == ExpandCollapseState.Expanded)
            .OrderBy(element => SafeBounds(element).Y)
            .FirstOrDefault();
        if (expanded is not null)
        {
            return expanded;
        }

        var checkBox = GetChildren(popup).FirstOrDefault(IsToggleCheckBox);
        var checkBoxBounds = checkBox is null ? Rect.Empty : SafeBounds(checkBox);
        return directTriggers
            .OrderBy(element => GetControlType(element) == ControlType.MenuItem ? 0 : 1)
            .ThenBy(element => checkBoxBounds.IsEmpty
                ? SafeBounds(element).Y
                : Math.Abs(SafeBounds(element).Y - checkBoxBounds.Y))
            .ThenBy(element => SafeBounds(element).X)
            .FirstOrDefault();
    }

    private static AutomationElement[] WaitForSelectorControls(
        AutomationElement popup,
        AutomationElement selector,
        AutomationElement? advancedToggle,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(1000);
        AutomationElement[] controls = [];
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            controls = GetDescendants(popup)
                .Where(TestVisible)
                .Where(IsPopupTrigger)
                .Where(element => !SameElement(element, selector))
                .Where(element => !SameElement(element, advancedToggle))
                .DistinctBy(GetRuntimeKey)
                .OrderBy(element => SafeBounds(element).Y)
                .ThenBy(element => SafeBounds(element).X)
                .ToArray();
            if (controls.Length >= 2)
            {
                break;
            }

            Thread.Sleep(30);
        }

        return controls;
    }

    private static IReadOnlyList<OptionSource> GetDirectOptionSources(AutomationElement popup)
    {
        var groups = new Dictionary<string, DirectOptionGroup>(StringComparer.Ordinal);
        foreach (var item in GetDescendants(popup).Where(TestVisible).Where(IsOptionElement))
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
                group = new DirectOptionGroup(container, []);
                groups.Add(key, group);
            }

            group.Items.Add(item);
        }

        var sources = new List<OptionSource>();
        foreach (var group in groups.Values)
        {
            var entries = BuildMenuEntries(group.Items, effort: false);
            if (entries.Count is >= 2 and <= 20)
            {
                sources.Add(new OptionSource(group.Container, ToMenuOptions(entries), RequiresOpen: false));
            }
        }

        return sources;
    }

    private static AutomationElement? FindSpeedToggle(AutomationElement popup) =>
        GetDescendants(popup)
            .Where(TestVisible)
            .Where(IsToggleCheckBox)
            .OrderBy(element => SafeBounds(element).Y)
            .ThenBy(element => SafeBounds(element).X)
            .FirstOrDefault();

    private static bool IsToggleCheckBox(AutomationElement element) =>
        GetControlType(element) == ControlType.CheckBox &&
        TryGetPattern(element, TogglePattern.Pattern, out _);

    private static ControlType? GetControlType(AutomationElement element)
    {
        try
        {
            return element.Current.ControlType;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<AutomationElement> GetChildren(AutomationElement container)
    {
        try
        {
            var nodes = container.FindAll(TreeScope.Children, Condition.TrueCondition);
            var result = new List<AutomationElement>(nodes.Count);
            for (var index = 0; index < nodes.Count; index++)
            {
                result.Add(nodes[index]);
            }

            return result;
        }
        catch
        {
            return Array.Empty<AutomationElement>();
        }
    }

    private static bool LooksLikeModelOptions(IReadOnlyList<string> labels, string selectorName)
    {
        if (labels.Count < 2)
        {
            return false;
        }

        if (labels.Any(label =>
                string.Equals(selectorName, label, StringComparison.Ordinal) ||
                selectorName.StartsWith(label + " ", StringComparison.Ordinal)))
        {
            return true;
        }

        var versionLike = labels.Count(label =>
            ModelLabelRegex().IsMatch(label) || Regex.IsMatch(label, @"\d+(?:\.\d+)+", RegexOptions.CultureInvariant));
        return versionLike >= Math.Max(2, labels.Count - 1);
    }

    private static MenuOptions NormalizeMenuOptions(MenuOptions options, bool effort) =>
        new(
            options.Items,
            options.Labels.Select(label => TextNormalizer.Normalize(label, effort)).ToArray());

    private static MenuOptions GetLiveOptions(
        int processId,
        OptionSource source,
        int minimum,
        int maximum,
        bool effort,
        CancellationToken cancellationToken)
    {
        if (source.RequiresOpen)
        {
            return GetMenuOptions(processId, source.Control, minimum, maximum, effort, cancellationToken);
        }

        var entries = BuildMenuEntries(
            GetDescendants(source.Control).Append(source.Control),
            effort);
        if (entries.Count < minimum || entries.Count > maximum)
        {
            throw new AutomationUnavailableException("The inline selector options are no longer exposed.");
        }

        return ToMenuOptions(entries);
    }

    private static void CloseSelectorLayout(
        AutomationElement selector,
        AutomationElement? advancedToggle,
        bool restoreNormalLayout,
        IReadOnlyList<AutomationElement> expandableControls)
    {
        foreach (var control in expandableControls.Reverse())
        {
            CloseSilent(control);
        }

        if (restoreNormalLayout)
        {
            CloseSilent(advancedToggle);
        }

        CloseSilent(selector);
    }

    private static MenuOptions GetMenuOptions(
        int processId,
        AutomationElement submenu,
        int minimum,
        int maximum,
        bool effort,
        CancellationToken cancellationToken)
    {
        if (!IsPopupTrigger(submenu))
        {
            var inlineEntries = BuildMenuEntries(
                GetDescendants(submenu).Append(submenu),
                effort);
            if (inlineEntries.Count < minimum || inlineEntries.Count > maximum)
            {
                throw new AutomationUnavailableException("The inline selector options are not exposed.");
            }

            return ToMenuOptions(inlineEntries);
        }

        CloseSilent(submenu);
        var visibleBefore = GetVisibleRuntimeKeys(processId);
        var submenuBounds = SafeBounds(submenu);
        OpenSilent(submenu, cancellationToken);
        var deadline = DateTime.UtcNow.AddMilliseconds(1100);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var visible = GetElements(processId).Where(TestVisible).ToArray();
            var newEntries = BuildMenuEntries(
                visible.Where(element =>
                {
                    var key = GetRuntimeKey(element);
                    return (key is null || !visibleBefore.Contains(key)) &&
                           IsNearSubmenuPopup(element, submenuBounds);
                }),
                effort);

            if (newEntries.Count >= minimum && newEntries.Count <= maximum)
            {
                return ToMenuOptions(newEntries);
            }

            var candidates = new List<MenuOptionCandidate>();
            foreach (var container in visible.Where(IsPopupContainer))
            {
                var bounds = SafeBounds(container);
                if (!IsNearSubmenuPopup(bounds, submenuBounds))
                {
                    continue;
                }

                var entries = BuildMenuEntries(
                    GetDescendants(container).Where(element =>
                        IsNearSubmenuPopup(element, submenuBounds)),
                    effort);
                if (entries.Count < minimum || entries.Count > maximum)
                {
                    continue;
                }

                var key = GetRuntimeKey(container);
                var isNew = key is null || !visibleBefore.Contains(key);
                candidates.Add(new MenuOptionCandidate(
                    entries,
                    ScoreContainer(bounds, submenuBounds, isNew, GetAutomationId(container))));
            }

            var best = candidates.OrderBy(static candidate => candidate.Score).FirstOrDefault();
            if (best is not null)
            {
                return ToMenuOptions(best.Entries);
            }

            Thread.Sleep(30);
        }

        throw new AutomationUnavailableException("The submenu options are not exposed.");
    }

    private static IReadOnlyList<AutomationElement> GetDescendants(AutomationElement container)
    {
        try
        {
            var nodes = container.FindAll(
                TreeScope.Descendants,
                Condition.TrueCondition);
            var result = new List<AutomationElement>(nodes.Count);
            for (var index = 0; index < nodes.Count; index++)
            {
                result.Add(nodes[index]);
            }

            return result;
        }
        catch
        {
            return Array.Empty<AutomationElement>();
        }
    }

    private static List<MenuEntry> BuildMenuEntries(
        IEnumerable<AutomationElement> elements,
        bool effort)
    {
        var entries = new List<MenuEntry>();
        var runtimeKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in elements.Where(TestVisible).Where(IsOptionElement))
        {
            var runtimeKey = GetRuntimeKey(item);
            if (runtimeKey is not null && !runtimeKeys.Add(runtimeKey))
            {
                continue;
            }

            var bounds = SafeBounds(item);
            if (bounds.IsEmpty)
            {
                continue;
            }

            var label = GetLabel(item, effort);
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var duplicate = entries.Any(existing =>
                string.Equals(existing.Label, label, StringComparison.Ordinal) &&
                Math.Abs(existing.X - bounds.X) < 2 &&
                Math.Abs(existing.Y - bounds.Y) < 2);
            if (!duplicate)
            {
                entries.Add(new MenuEntry(item, bounds.X, bounds.Y, label));
            }
        }

        entries.Sort(static (left, right) =>
        {
            var byY = left.Y.CompareTo(right.Y);
            return byY != 0 ? byY : left.X.CompareTo(right.X);
        });
        return entries;
    }

    private static MenuOptions ToMenuOptions(IReadOnlyList<MenuEntry> entries) =>
        new(
            entries.Select(static entry => entry.Item).ToArray(),
            entries.Select(static entry => entry.Label).ToArray());

    private static Rect SafeBounds(AutomationElement element)
    {
        try
        {
            return element.Current.BoundingRectangle;
        }
        catch
        {
            return Rect.Empty;
        }
    }

    private static bool IsNearSelectorPopup(AutomationElement element, AutomationElement selector)
    {
        var candidate = SafeBounds(element);
        var anchor = SafeBounds(selector);
        if (candidate.IsEmpty || anchor.IsEmpty)
        {
            return false;
        }

        var dx = Math.Abs(candidate.X + candidate.Width / 2 - (anchor.X + anchor.Width / 2));
        var dy = Math.Abs(candidate.Y + candidate.Height / 2 - (anchor.Y + anchor.Height / 2));
        return dx <= 420 && dy <= 520;
    }

    private static bool IsNearSubmenuPopup(AutomationElement element, Rect submenuBounds) =>
        IsNearSubmenuPopup(SafeBounds(element), submenuBounds);

    private static bool IsNearSubmenuPopup(Rect candidate, Rect submenuBounds)
    {
        if (candidate.IsEmpty || submenuBounds.IsEmpty)
        {
            return false;
        }

        var dx = Math.Abs(candidate.X + candidate.Width / 2 -
                          (submenuBounds.X + submenuBounds.Width / 2));
        var dy = Math.Abs(candidate.Y + candidate.Height / 2 -
                          (submenuBounds.Y + submenuBounds.Height / 2));
        return dx <= 620 && dy <= 420;
    }

    private static double ScoreContainer(
        Rect bounds,
        Rect submenuBounds,
        bool isNew,
        string automationId)
    {
        var newPenalty = isNew ? 0 : 1_000_000;
        var identityPenalty = string.IsNullOrWhiteSpace(automationId) ? 5_000 : 0;
        if (bounds.IsEmpty)
        {
            return newPenalty + identityPenalty + 900_000;
        }

        var area = Math.Min(bounds.Width * bounds.Height, 500_000);
        var dx = bounds.X + bounds.Width / 2 - (submenuBounds.X + submenuBounds.Width / 2);
        var dy = bounds.Y + bounds.Height / 2 - (submenuBounds.Y + submenuBounds.Height / 2);
        return newPenalty + identityPenalty + area + Math.Sqrt(dx * dx + dy * dy) * 100;
    }

    private sealed record DirectOptionGroup(AutomationElement Container, List<AutomationElement> Items);
}
