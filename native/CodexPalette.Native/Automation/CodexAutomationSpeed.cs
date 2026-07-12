using System.Windows.Automation;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private int _lastKnownSpeedIndex = -1;

    private SpeedDescriptor GetSpeed(
        int processId,
        AutomationContext context,
        CancellationToken cancellationToken)
    {
        var control = context.SpeedMenu ??
            throw new AutomationUnavailableException(
                "The native speed control is not exposed in the Codex selector popup.");

        if (context.SpeedToggle is not null && SameElement(control, context.SpeedToggle))
        {
            var cached = GetCachedDiscovery();
            var labels = cached.Speeds.Count == 2
                ? cached.Speeds.ToArray()
                : GetToggleSpeedLabels(control);
            var label = !string.IsNullOrWhiteSpace(cached.SpeedLabel)
                ? cached.SpeedLabel
                : GetToggleSpeedGroupLabel(control);
            var selectedIndex = GetToggleState(control) == ToggleState.On ? 1 : 0;
            Volatile.Write(ref _lastKnownSpeedIndex, selectedIndex);
            return new SpeedDescriptor(
                context.Selector,
                control,
                Array.Empty<AutomationElement>(),
                labels,
                label,
                selectedIndex,
                IsToggle: true);
        }

        var options = GetMenuOptions(
            processId,
            control,
            minimum: 2,
            maximum: 2,
            effort: false,
            cancellationToken);
        if (options.Labels.Count != 2)
        {
            throw new AutomationUnavailableException(
                "The native speed selector does not expose exactly two options.");
        }

        var descriptor = NewSpeed(context.Selector, control, options);
        Volatile.Write(ref _lastKnownSpeedIndex, descriptor.SelectedIndex);
        return descriptor;
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
            TextNormalizer.GetGroupLabel(SafeName(control), texts, options.Labels),
            GetSelectedIndex(control, options.Items, options.Labels),
            IsToggle: false);
    }

    private static string[] GetToggleSpeedLabels(AutomationElement control)
    {
        var value = GetToggleValue(control);
        var state = GetToggleState(control);
        if (!string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "0", StringComparison.Ordinal) &&
            !string.Equals(value, "1", StringComparison.Ordinal))
        {
            return state == ToggleState.On
                ? ["Off", value]
                : [value, "On"];
        }

        return ["Off", "On"];
    }

    private static string GetToggleSpeedGroupLabel(AutomationElement control)
    {
        var name = TextNormalizer.Normalize(SafeName(control));
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var texts = GetTexts(control);
        return texts.FirstOrDefault() ?? "Speed";
    }

    private static string GetToggleValue(AutomationElement control)
    {
        if (!TryGetPattern(control, ValuePattern.Pattern, out var valuePattern))
        {
            return string.Empty;
        }

        try
        {
            return TextNormalizer.Normalize(((ValuePattern)valuePattern).Current.Value);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static ToggleState GetToggleState(AutomationElement control)
    {
        if (!TryGetPattern(control, TogglePattern.Pattern, out var toggleValue))
        {
            return ToggleState.Indeterminate;
        }

        try
        {
            return ((TogglePattern)toggleValue).Current.ToggleState;
        }
        catch
        {
            return ToggleState.Indeterminate;
        }
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

        var ownerName = TextNormalizer.Normalize(SafeName(owner));
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
