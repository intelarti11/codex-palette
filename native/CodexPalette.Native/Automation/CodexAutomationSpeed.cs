using System.Windows.Automation;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{    private static SpeedDescriptor GetSpeed(
        int processId,
        AutomationContext context,
        CancellationToken cancellationToken)
    {
        foreach (var submenu in context.Submenus)
        {
            if (SameElement(submenu, context.ModelMenu) || SameElement(submenu, context.EffortMenu))
            {
                continue;
            }

            try
            {
                var options = GetMenuOptions(processId, submenu, 2, 2, effort: false, cancellationToken);
                if (options.Labels.Count == 2)
                {
                    return NewSpeed(context.Selector, submenu, options);
                }
            }
            catch
            {
                CloseSilent(submenu);
            }
        }

        CloseSilent(context.Selector);
        var selector = FindElement(
            processId,
            ControlType.Button,
            SelectorRegex().ToString(),
            exact: false,
            timeoutMilliseconds: 2500,
            cancellationToken);
        var selectorBounds = selector.Current.BoundingRectangle;
        var centerX = selectorBounds.X + selectorBounds.Width / 2;
        var centerY = selectorBounds.Y + selectorBounds.Height / 2;

        var candidates = GetElements(processId, ControlType.Button)
            .Where(button => !SameElement(button, selector) && TestVisible(button))
            .Select(button =>
            {
                if (!TryGetPattern(button, ExpandCollapsePattern.Pattern, out var value))
                {
                    return null;
                }

                var pattern = (ExpandCollapsePattern)value;
                if (pattern.Current.ExpandCollapseState == ExpandCollapseState.LeafNode)
                {
                    return null;
                }

                var bounds = button.Current.BoundingRectangle;
                var dx = Math.Abs(bounds.X + bounds.Width / 2 - centerX);
                var dy = Math.Abs(bounds.Y + bounds.Height / 2 - centerY);
                return dx <= 520 && dy <= 140
                    ? new SpeedCandidate(button, dx + 3 * dy)
                    : null;
            })
            .Where(static candidate => candidate is not null)
            .Cast<SpeedCandidate>()
            .OrderBy(static candidate => candidate.Distance)
            .ToArray();

        foreach (var candidate in candidates)
        {
            try
            {
                var options = GetMenuOptions(processId, candidate.Button, 2, 2, effort: false, cancellationToken);
                if (options.Labels.Count == 2)
                {
                    return NewSpeed(candidate.Button, candidate.Button, options);
                }
            }
            catch
            {
                CloseSilent(candidate.Button);
            }
        }

        throw new AutomationUnavailableException("The native two-position speed selector is not exposed.");
    }

    private static SpeedDescriptor NewSpeed(
        AutomationElement owner,
        AutomationElement control,
        MenuOptions options)
    {
        var texts = GetTexts(control);
        return new SpeedDescriptor(
            owner,
            control,
            options.Items,
            options.Labels,
            TextNormalizer.GetGroupLabel(control.Current.Name, texts, options.Labels),
            GetSelectedIndex(control, options.Items, options.Labels));
    }

    private static int GetSelectedIndex(
        AutomationElement owner,
        IReadOnlyList<AutomationElement> items,
        IReadOnlyList<string> labels)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (TryGetPattern(items[index], SelectionItemPattern.Pattern, out var selectionValue) &&
                ((SelectionItemPattern)selectionValue).Current.IsSelected)
            {
                return index;
            }

            if (TryGetPattern(items[index], TogglePattern.Pattern, out var toggleValue) &&
                ((TogglePattern)toggleValue).Current.ToggleState == ToggleState.On)
            {
                return index;
            }
        }

        var ownerName = TextNormalizer.Normalize(owner.Current.Name);
        var texts = GetTexts(owner);
        for (var index = 0; index < labels.Count; index++)
        {
            if (ownerName.EndsWith(labels[index], StringComparison.Ordinal) ||
                texts.Contains(labels[index], StringComparer.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }


}
