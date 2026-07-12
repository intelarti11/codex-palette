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

        var deadline = DateTime.UtcNow.AddMilliseconds(900);
        AutomationElement[] submenus = [];
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var visible = GetElements(processId)
                .Where(TestVisible)
                .Where(element => !SameElement(element, selector))
                .Where(IsPopupTrigger)
                .Where(element => IsNearSelectorPopup(element, selector))
                .ToArray();

            var revealed = visible.Where(element =>
            {
                var key = GetRuntimeKey(element);
                return key is null || !visibleBefore.Contains(key);
            }).ToArray();

            submenus = (revealed.Length >= 2 ? revealed : visible)
                .OrderBy(element => SafeBounds(element).Y)
                .ThenBy(element => SafeBounds(element).X)
                .ToArray();
            if (submenus.Length >= 2)
            {
                break;
            }

            Thread.Sleep(30);
        }

        AutomationElement? modelMenu = null;
        AutomationElement? effortMenu = null;
        AutomationElement? speedMenu = null;
        MenuOptions? modelOptions = null;
        MenuOptions? effortOptions = null;

        foreach (var submenu in submenus)
        {
            try
            {
                var options = GetMenuOptions(processId, submenu, 1, 20, false, cancellationToken);
                CloseSilent(submenu);
                var modelLike = options.Labels.Count >= 2 &&
                    options.Labels.Count(label => ModelLabelRegex().IsMatch(label)) >=
                    Math.Max(2, options.Labels.Count - 1);
                if (modelLike && modelMenu is null)
                {
                    modelMenu = submenu;
                    modelOptions = options;
                    continue;
                }

                var matchesCurrentSuffix = options.Labels.Any(label =>
                    selectorName.EndsWith(" " + label, StringComparison.Ordinal));
                if (matchesCurrentSuffix && options.Labels.Count >= 2 && effortMenu is null)
                {
                    effortMenu = submenu;
                    effortOptions = options;
                    continue;
                }

                if (options.Labels.Count == 2 && speedMenu is null)
                {
                    speedMenu = submenu;
                }
            }
            catch
            {
                CloseSilent(submenu);
            }
        }

        if (modelMenu is null || effortMenu is null || modelOptions is null || effortOptions is null)
        {
            CloseSilent(selector);
            throw new AutomationUnavailableException(
                "The native model or effort selector is not exposed through ExpandCollapsePattern.");
        }

        var model = modelOptions.Labels
            .OrderByDescending(static value => value.Length)
            .FirstOrDefault(value => selectorName.StartsWith(value + " ", StringComparison.Ordinal)) ??
            throw new AutomationUnavailableException("The current model could not be identified from native options.");
        var effort = selectorName[model.Length..].Trim();

        return new AutomationContext(
            selector,
            model,
            effort,
            submenus,
            modelMenu,
            effortMenu,
            speedMenu,
            modelOptions,
            effortOptions);
    }

    private static MenuOptions GetMenuOptions(
        int processId,
        AutomationElement submenu,
        int minimum,
        int maximum,
        bool effort,
        CancellationToken cancellationToken)
    {
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
                System.Windows.Automation.Condition.TrueCondition);
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
}
