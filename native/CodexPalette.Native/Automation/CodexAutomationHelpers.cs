using System.Windows.Automation;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private static IReadOnlyList<string> GetTexts(AutomationElement element)
    {
        var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
        var nodes = element.FindAll(TreeScope.Descendants, condition);
        var values = new List<string>();

        for (var index = 0; index < nodes.Count; index++)
        {
            var value = TextNormalizer.Normalize(nodes[index].Current.Name);
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.Ordinal))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string GetLabel(AutomationElement element, bool effort)
    {
        var texts = GetTexts(element);
        var value = texts.Count > 0 ? texts[0] : element.Current.Name;
        return TextNormalizer.Normalize(value, effort);
    }

    private static int FindLabelIndex(IReadOnlyList<string> labels, string selectedLabel)
    {
        var normalizedSelected = TextNormalizer.Normalize(selectedLabel, effort: true);
        for (var index = 0; index < labels.Count; index++)
        {
            if (string.Equals(
                    TextNormalizer.Normalize(labels[index], effort: true),
                    normalizedSelected,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryGetPattern(AutomationElement element, AutomationPattern pattern, out object value)
    {
        value = null!;
        try
        {
            return element.TryGetCurrentPattern(pattern, out value);
        }
        catch
        {
            return false;
        }
    }

    private static string? GetRuntimeKey(AutomationElement element)
    {
        try
        {
            return string.Join(".", element.GetRuntimeId());
        }
        catch
        {
            return null;
        }
    }

    private static bool SameElement(AutomationElement? left, AutomationElement? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        var leftKey = GetRuntimeKey(left);
        var rightKey = GetRuntimeKey(right);
        return leftKey is not null && string.Equals(leftKey, rightKey, StringComparison.Ordinal);
    }

    private sealed record AutomationContext(
        AutomationElement Selector,
        string Model,
        string Effort,
        IReadOnlyList<AutomationElement> Submenus,
        AutomationElement ModelMenu,
        AutomationElement EffortMenu);

    private sealed record MenuEntry(AutomationElement Item, double X, double Y, string Label);
    private sealed record MenuOptions(IReadOnlyList<AutomationElement> Items, IReadOnlyList<string> Labels);
    private sealed record SpeedCandidate(AutomationElement Button, double Distance);
    private sealed record SpeedDescriptor(
        AutomationElement Owner,
        AutomationElement Control,
        IReadOnlyList<AutomationElement> Items,
        IReadOnlyList<string> Labels,
        string Label,
        int SelectedIndex);
}
