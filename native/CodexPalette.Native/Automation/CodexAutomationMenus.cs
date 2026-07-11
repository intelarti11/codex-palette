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
            .Where(IsPotentialSubmenu)
            .ToArray();

        var submenus = revealed.Length > 0
            ? revealed
            : allVisible.Where(IsPopupTrigger).ToArray();

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
        foreach (var candidate in candidates.Where(IsPopupTrigger))
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
                    maximum: 8,
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
                // Not one of the selector's value submenus.
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
        OpenSilent(submenu, cancellationToken);
        var deadline = DateTime.UtcNow.AddMilliseconds(2200);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var visible = GetElements(processId).Where(TestVisible).ToArray();

            var newlyVisibleEntries = BuildMenuEntries(
                visible.Where(element =>
                {
                    var key = GetRuntimeKey(element);
                    return key is null || !visibleBefore.Contains(key);
                }),
                effort);

            if (newlyVisibleEntries.Count >= minimum && newlyVisibleEntries.Count <= maximum)
            {
                return ToMenuOptions(newlyVisibleEntries);
            }

            var submenuBounds = SafeBounds(submenu);
            var candidates = new List<MenuOptionCandidate>();

            foreach (var container in visible.Where(IsPopupContainer))
            {
                var entries = BuildMenuEntries(GetDescendants(container), effort);
                if (entries.Count < minimum || entries.Count > maximum)
                {
                    continue;
                }

                var key = GetRuntimeKey(container);
                var isNew = key is null || !visibleBefore.Contains(key);
                var bounds = SafeBounds(container);
                var score = ScoreContainer(bounds, submenuBounds, isNew, GetAutomationId(container));
                candidates.Add(new MenuOptionCandidate(entries, score));
            }

            var best = candidates.OrderBy(static candidate => candidate.Score).FirstOrDefault();
            if (best is not null)
            {
                return ToMenuOptions(best.Entries);
            }

            Thread.Sleep(70);
        }

        throw new AutomationUnavailableException("The submenu options are not exposed.");
    }

    private static IReadOnlyList<AutomationElement> GetDescendants(AutomationElement container)
    {
        try
        {
            var nodes = container.FindAll(TreeScope.Descendants, Condition.TrueCondition);
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
