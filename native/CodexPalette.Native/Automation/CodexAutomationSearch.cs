using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private static AutomationElement FindElement(
        int processId,
        ControlType controlType,
        string name,
        bool exact,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        var regex = exact ? null : new Regex(name, RegexOptions.CultureInvariant);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = controlType == ControlType.MenuItem
                ? GetElements(processId).Where(TestVisible).Where(IsOptionElement)
                : GetElements(processId, controlType);
            foreach (var element in candidates)
            {
                if (MatchesLabel(element, name, exact, regex))
                {
                    return element;
                }
            }

            Thread.Sleep(70);
        }

        throw new AutomationUnavailableException($"Codex control not found: {name}");
    }

    private static AutomationElement FindActionableElement(
        int processId,
        string name,
        bool exact,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        var regex = exact ? null : new Regex(name, RegexOptions.CultureInvariant);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var element in GetElements(processId).Where(TestVisible).Where(IsOptionElement))
            {
                if (MatchesLabel(element, name, exact, regex))
                {
                    return element;
                }
            }

            Thread.Sleep(70);
        }

        throw new AutomationUnavailableException($"Codex action not found: {name}");
    }

    private static bool MatchesLabel(
        AutomationElement element,
        string name,
        bool exact,
        Regex? regex)
    {
        foreach (var candidate in GetElementStrings(element))
        {
            var matches = exact
                ? string.Equals(candidate, name, StringComparison.Ordinal)
                : regex!.IsMatch(candidate);
            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<AutomationElement> GetElements(int processId)
    {
        var condition = new PropertyCondition(AutomationElement.ProcessIdProperty, processId);
        var all = AutomationElement.RootElement.FindAll(TreeScope.Descendants, condition);
        var result = new List<AutomationElement>(all.Count);

        for (var index = 0; index < all.Count; index++)
        {
            try
            {
                result.Add(all[index]);
            }
            catch
            {
                // Ignore elements invalidated while traversing Chromium's dynamic tree.
            }
        }

        return result;
    }

    private static IReadOnlyList<AutomationElement> GetElements(int processId, ControlType controlType) =>
        GetElements(processId)
            .Where(element =>
            {
                try
                {
                    return element.Current.ControlType == controlType;
                }
                catch
                {
                    return false;
                }
            })
            .ToArray();

    private static HashSet<string> GetVisibleRuntimeKeys(int processId) =>
        GetElements(processId)
            .Where(TestVisible)
            .Select(GetRuntimeKey)
            .Where(static key => key is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsPopupTrigger(AutomationElement element)
    {
        if (!TryGetPattern(element, ExpandCollapsePattern.Pattern, out var value))
        {
            return false;
        }

        try
        {
            return ((ExpandCollapsePattern)value).Current.ExpandCollapseState != ExpandCollapseState.LeafNode;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPotentialSubmenu(AutomationElement element) =>
        IsPopupTrigger(element) || TryGetPattern(element, InvokePattern.Pattern, out _);

    private static bool IsOptionElement(AutomationElement element)
    {
        if (IsPopupTrigger(element))
        {
            return false;
        }

        return TryGetPattern(element, SelectionItemPattern.Pattern, out _) ||
               TryGetPattern(element, TogglePattern.Pattern, out _) ||
               TryGetPattern(element, InvokePattern.Pattern, out _);
    }

    private static bool IsPopupContainer(AutomationElement element)
    {
        try
        {
            var type = element.Current.ControlType;
            return type == ControlType.Menu ||
                   type == ControlType.List ||
                   type == ControlType.Pane ||
                   type == ControlType.Group ||
                   type == ControlType.Custom ||
                   type == ControlType.Window ||
                   TryGetPattern(element, SelectionPattern.Pattern, out _);
        }
        catch
        {
            return false;
        }
    }

    private static string GetAutomationId(AutomationElement element) =>
        GetStringProperty(element, AutomationElement.AutomationIdProperty);

    private static string GetStringProperty(AutomationElement element, AutomationProperty property)
    {
        try
        {
            var value = element.GetCurrentPropertyValue(property, ignoreDefaultValue: true);
            return ReferenceEquals(value, AutomationElement.NotSupported)
                ? string.Empty
                : TextNormalizer.Normalize(value as string);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<string> GetElementStrings(AutomationElement element)
    {
        var values = new List<string>();

        static void Add(List<string> target, string? value)
        {
            var normalized = TextNormalizer.Normalize(value);
            if (!string.IsNullOrWhiteSpace(normalized) && !target.Contains(normalized, StringComparer.Ordinal))
            {
                target.Add(normalized);
            }
        }

        try
        {
            Add(values, element.Current.Name);
        }
        catch
        {
            // The element may have disappeared between enumeration and inspection.
        }

        foreach (var text in GetTexts(element))
        {
            Add(values, text);
        }

        try
        {
            var labeledBy = element.Current.LabeledBy;
            if (labeledBy is not null)
            {
                Add(values, labeledBy.Current.Name);
            }
        }
        catch
        {
            // Optional relationship.
        }

        Add(values, GetStringProperty(element, AutomationElement.ItemStatusProperty));
        Add(values, GetStringProperty(element, AutomationElement.HelpTextProperty));

        if (TryGetPattern(element, ValuePattern.Pattern, out var valuePattern))
        {
            try
            {
                Add(values, ((ValuePattern)valuePattern).Current.Value);
            }
            catch
            {
                // Optional value.
            }
        }

        return values;
    }

    private static bool ElementContains(AutomationElement element, string value) =>
        GetElementStrings(element).Any(candidate =>
            candidate.Contains(value, StringComparison.Ordinal));

    private static bool TestVisible(AutomationElement element)
    {
        try
        {
            return !element.Current.IsOffscreen && !element.Current.BoundingRectangle.IsEmpty;
        }
        catch
        {
            return false;
        }
    }
}
