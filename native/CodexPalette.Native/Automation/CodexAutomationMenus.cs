using System.Windows;
using System.Windows.Automation;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private static AutomationContext GetContext(int processId, CancellationToken cancellationToken)
    {
        var selector = FindElement(
            processId,
            ControlType.Button,
            SelectorRegex().ToString(),
            exact: false,
            timeoutMilliseconds: 2500,
            cancellationToken);
        var selectorName = TextNormalizer.Normalize(selector.Current.Name);
        var model = ModelNames.FirstOrDefault(name =>
            selectorName.StartsWith(name + " ", StringComparison.Ordinal));
        if (model is null)
        {
            throw new AutomationUnavailableException("The current model could not be identified.");
        }

        var effort = selectorName[model.Length..].Trim();
        var visibleBefore = GetVisibleRuntimeKeys(processId);
        OpenSilent(selector, cancellationToken);

        var allVisible = GetElements(processId)
            .Where(TestVisible)
            .Where(element => !SameElement(element, selector))
            .ToArray();

        var revealed = allVisible
            .Where(element =>
            {
                var key = GetRuntimeKey(element);
                return key is null || !visibleBefore.Contains(key);
            })
            .Where(IsCollapsedPopupTrigger)
            .Where(element => IsNearSelectorPopup(element, selector))
            .ToArray();

        var submenus = (revealed.Length > 0
                ? revealed
                : allVisible
                    .Where(IsCollapsedPopupTrigger)
                    .Where(element => IsNearSelectorPopup(element, selector)))
            .OrderBy(element => SafeBounds(element).Y)
            .ThenBy(element => SafeBounds(element).X)
            .ToArray();

        var modelMenu = submenus.FirstOrDefault(element => ElementContains(element, model));
        var effortMenu = submenus.FirstOrDefault(element =>
            !SameElement(element, modelMenu) && ElementContains(element, effort));

        if (modelMenu is null || effortMenu is null)
        {
            DiscoverSubmenusByStructure(
                processId,
                submenus,
                model,
                effort,
                ref modelMenu,
                ref effortMenu,
                cancellationToken);
        }

        if (modelMenu is null || effortMenu is null)
        {
            CloseSilent(selector);
            throw new AutomationUnavailableException(
                "The native model or reasoning submenu is not exposed through UI Automation patterns.");
        }

        return new AutomationContext(selector, model, effort, submenus, modelMenu, effortMenu);
    }

    private static void DiscoverSubmenusByStructure(
        int processId,
        IReadOnlyList<AutomationElement> candidates,
        string model,
        string effort,
        ref AutomationElement? modelMenu,
        ref AutomationElement? effortMenu,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates.Where(IsCollapsedPopupTrigger))
        {
            if (modelMenu is not null && effortMenu is not null)
            {
                return;
            }

            try
            {
                var options = GetMenuOptions(
                    processId,
                    candidate,
                    minimum: 2,
                    maximum: 10,
                    effort: false,
                    cancellationToken);

                if (modelMenu is null && options.Labels.Any(label =>
                        string.Equals(label, model, StringComparison.Ordinal)))
                {
                    modelMenu = candidate;
                }
                else if (effortMenu is null && options.Labels.Any(label =>
                             string.Equals(
                                 TextNormalizer.Normalize(label, effort: true),
                                 TextNormalizer.Normalize(effort, effort: true),
                                 StringComparison.Ordinal)))
                {
                    effortMenu = candidate;
                }
            }
            catch
            {
                // The candidate belongs to the selector popup but is not model or effort.
            }
            finally
            {
                CloseSilent(candidate);
            }
        }
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
        var deadline = DateTime.UtcNow.AddMilliseconds(1600);

        try
        {
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var visible = GetElements(processId).Where(TestVisible).ToArray();

                var newlyVisibleEntries = BuildMenuEntries(
                    visible.Where(element =>
                    {
                        var key = GetRuntimeKey(element);
                        return (key is null || !visibleBefore.Contains(key)) &&
                               IsNearSubmenuPopup(element, submenuBounds);
                    }),
                    effort);

                if (newlyVisibleEntries.Count >= minimum && newlyVisibleEntries.Count <= maximum)
                {
                    return ToMenuOptions(newlyVisibleEntries);
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
                    var score = ScoreContainer(bounds, submenuBounds, isNew, GetAutomationId(container));
                    candidates.Add(new MenuOptionCandidate(entries, score));
                }

                var best = candidates.OrderBy(static candidate => candidate.Score).FirstOrDefault();
                if (best is not null)
                {
                    return ToMenuOptions(best.Entries);
                }

                Thread.Sleep(35);
            }
        }
        catch
        {
            CloseSilent(submenu);
            throw;
        }

        CloseSilent(submenu);
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
        return dx <= 560 && dy <= 620;
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
        return dx <= 760 && dy <= 520;
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
        if (submenuBounds.IsEmpty)
        {
            return newPenalty + identityPenalty + area;
        }

        var dx = bounds.X + bounds.Width / 2 - (submenuBounds.X + submenuBounds.Width / 2);
        var dy = bounds.Y + bounds.Height / 2 - (submenuBounds.Y + submenuBounds.Height / 2);
        return newPenalty + identityPenalty + area + Math.Sqrt(dx * dx + dy * dy) * 100;
    }
}