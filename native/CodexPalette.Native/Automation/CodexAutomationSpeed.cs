using System.Windows.Automation;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private static SpeedDescriptor GetSpeed(
        int processId,
        AutomationContext context,
        CancellationToken cancellationToken)
    {
        var candidates = context.Submenus
            .Where(IsCollapsedPopupTrigger)
            .Where(submenu =>
                !SameElement(submenu, context.ModelMenu) &&
                !SameElement(submenu, context.EffortMenu))
            .ToArray();

        foreach (var submenu in candidates)
        {
            try
            {
                var options = GetMenuOptions(
                    processId,
                    submenu,
                    minimum: 2,
                    maximum: 2,
                    effort: false,
                    cancellationToken);
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

        throw new AutomationUnavailableException(
            "The native two-position speed selector was not found inside the Codex selector popup.");
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