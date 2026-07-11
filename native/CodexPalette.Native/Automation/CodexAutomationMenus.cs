using System.Windows.Automation;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{    private static AutomationContext GetContext(int processId, CancellationToken cancellationToken)
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
        OpenSilent(selector, cancellationToken);

        var submenus = GetElements(processId, ControlType.MenuItem)
            .Where(TestVisible)
            .Where(element =>
            {
                if (!TryGetPattern(element, ExpandCollapsePattern.Pattern, out var value))
                {
                    return false;
                }

                return ((ExpandCollapsePattern)value).Current.ExpandCollapseState != ExpandCollapseState.LeafNode;
            })
            .ToArray();

        var modelMenu = submenus.FirstOrDefault(element =>
            TextNormalizer.Normalize(element.Current.Name).Contains(model, StringComparison.Ordinal));
        var effortMenu = submenus.FirstOrDefault(element =>
            !SameElement(element, modelMenu) &&
            TextNormalizer.Normalize(element.Current.Name).EndsWith(effort, StringComparison.Ordinal));

        if (modelMenu is null || effortMenu is null)
        {
            CloseSilent(selector);
            throw new AutomationUnavailableException("The native model or reasoning submenu is not exposed.");
        }

        return new AutomationContext(selector, model, effort, submenus, modelMenu, effortMenu);
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
        var existingMenus = GetElements(processId, ControlType.Menu)
            .Where(TestVisible)
            .Select(GetRuntimeKey)
            .Where(static key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        OpenSilent(submenu, cancellationToken);
        var deadline = DateTime.UtcNow.AddMilliseconds(2000);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MenuOptions? fallback = null;

            foreach (var menu in GetElements(processId, ControlType.Menu).Where(TestVisible))
            {
                var condition = new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.MenuItem);
                var nodes = menu.FindAll(TreeScope.Descendants, condition);
                var entries = new List<MenuEntry>();

                for (var index = 0; index < nodes.Count; index++)
                {
                    var item = nodes[index];
                    if (!TestVisible(item))
                    {
                        continue;
                    }

                    var bounds = item.Current.BoundingRectangle;
                    entries.Add(new MenuEntry(item, bounds.X, bounds.Y, GetLabel(item, effort)));
                }

                entries.Sort(static (left, right) =>
                {
                    var byY = left.Y.CompareTo(right.Y);
                    return byY != 0 ? byY : left.X.CompareTo(right.X);
                });

                if (entries.Count < minimum || entries.Count > maximum)
                {
                    continue;
                }

                var result = new MenuOptions(
                    entries.Select(static entry => entry.Item).ToArray(),
                    entries.Select(static entry => entry.Label).ToArray());
                var runtimeKey = GetRuntimeKey(menu);
                if (runtimeKey is not null && !existingMenus.Contains(runtimeKey))
                {
                    return result;
                }

                fallback = result;
            }

            if (fallback is not null)
            {
                return fallback;
            }

            Thread.Sleep(70);
        }

        throw new AutomationUnavailableException("The submenu options are not exposed.");
    }


}
