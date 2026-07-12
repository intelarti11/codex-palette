using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Automation;
using CodexPalette.Native.Infrastructure;

namespace CodexPalette.Native.Automation;

internal static class PopupDiagnostics
{
    private static AutomationEventHandler? _handler;

    [ModuleInitializer]
    internal static void Initialize()
    {
        _handler = OnMenuOpened;
        try
        {
            global::System.Windows.Automation.Automation.AddAutomationEventHandler(
                AutomationElement.MenuOpenedEvent,
                AutomationElement.RootElement,
                TreeScope.Subtree,
                _handler);
            DiagnosticLog.Write("Immediate popup descendant diagnostics enabled.");
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Could not enable immediate popup diagnostics", exception);
        }
    }

    internal static void CaptureAfterExpand(AutomationElement expandedControl)
    {
        if (!IsChatGptElement(expandedControl))
        {
            return;
        }

        try
        {
            var menu = FindNearestMenu(expandedControl);
            if (menu is null || !HasDirectCheckBox(menu))
            {
                return;
            }

            // Chromium updates this selector inline rather than opening another native popup.
            Thread.Sleep(140);
            DiagnosticLog.Write("ADVANCED EXPANDED CONTROL: " + Describe(expandedControl));
            DiagnosticLog.Write("ADVANCED MENU ROOT: " + Describe(menu));
            DumpCollection(
                "ADVANCED DIRECT CHILD",
                menu.FindAll(TreeScope.Children, Condition.TrueCondition),
                60);
            DumpCollection(
                "ADVANCED DESCENDANT",
                menu.FindAll(TreeScope.Descendants, Condition.TrueCondition),
                180);
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Expanded selector dump failed", exception);
        }
    }

    private static void OnMenuOpened(object sender, AutomationEventArgs args)
    {
        if (sender is not AutomationElement menu || !IsChatGptElement(menu))
        {
            return;
        }

        try
        {
            DiagnosticLog.Write("POPUP OPEN ROOT: " + Describe(menu));
            DumpCollection("DIRECT CHILD", menu.FindAll(TreeScope.Children, Condition.TrueCondition), 40);
            DumpCollection("DESCENDANT", menu.FindAll(TreeScope.Descendants, Condition.TrueCondition), 100);
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Immediate popup dump failed", exception);
        }
    }

    private static AutomationElement? FindNearestMenu(AutomationElement element)
    {
        var walker = TreeWalker.ControlViewWalker;
        AutomationElement? current = element;
        for (var depth = 0; depth < 10 && current is not null; depth++)
        {
            try
            {
                if (current.Current.ControlType == ControlType.Menu)
                {
                    return current;
                }

                current = walker.GetParent(current);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static bool HasDirectCheckBox(AutomationElement menu)
    {
        try
        {
            var condition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.CheckBox);
            return menu.FindFirst(TreeScope.Children, condition) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void DumpCollection(string prefix, AutomationElementCollection elements, int maximum)
    {
        DiagnosticLog.Write($"POPUP {prefix} COUNT: {elements.Count}");
        var limit = Math.Min(elements.Count, maximum);
        for (var index = 0; index < limit; index++)
        {
            try
            {
                DiagnosticLog.Write($"  {prefix} #{index}: {Describe(elements[index])}");
            }
            catch (Exception exception)
            {
                DiagnosticLog.WriteException($"  {prefix} #{index} could not be read", exception);
            }
        }

        if (elements.Count > limit)
        {
            DiagnosticLog.Write($"  {prefix}: {elements.Count - limit} additional elements omitted.");
        }
    }

    private static bool IsChatGptElement(AutomationElement element)
    {
        try
        {
            using var process = Process.GetProcessById(element.Current.ProcessId);
            return string.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string Describe(AutomationElement element)
    {
        var current = element.Current;
        var values = new List<string>();
        Add(values, current.Name);
        Add(values, ReadStringProperty(element, AutomationElement.HelpTextProperty));
        Add(values, ReadStringProperty(element, AutomationElement.ItemStatusProperty));

        try
        {
            if (current.LabeledBy is not null)
            {
                Add(values, current.LabeledBy.Current.Name);
            }
        }
        catch
        {
            // Optional UIA relation.
        }

        var patterns = new List<string>();
        AddPattern(patterns, element, ExpandCollapsePattern.Pattern, "ExpandCollapse");
        AddPattern(patterns, element, InvokePattern.Pattern, "Invoke");
        AddPattern(patterns, element, SelectionItemPattern.Pattern, "SelectionItem");
        AddPattern(patterns, element, SelectionPattern.Pattern, "Selection");
        AddPattern(patterns, element, TogglePattern.Pattern, "Toggle");
        AddPattern(patterns, element, ValuePattern.Pattern, "Value");
        AddPattern(patterns, element, RangeValuePattern.Pattern, "RangeValue");

        var states = new List<string>();
        AddPatternState(states, element);

        var bounds = current.BoundingRectangle;
        var runtime = SafeRuntimeId(element);
        return $"pid={current.ProcessId} type={current.ControlType.ProgrammaticName} " +
               $"name=\"{Sanitize(string.Join(" | ", values))}\" " +
               $"id=\"{Sanitize(current.AutomationId)}\" class=\"{Sanitize(current.ClassName)}\" " +
               $"patterns=[{string.Join(",", patterns)}] states=[{string.Join(",", states)}] " +
               $"enabled={current.IsEnabled} offscreen={current.IsOffscreen} " +
               $"bounds={bounds.X:0},{bounds.Y:0},{bounds.Width:0},{bounds.Height:0} runtime={runtime}";
    }

    private static void AddPatternState(ICollection<string> states, AutomationElement element)
    {
        if (TryPattern(element, ExpandCollapsePattern.Pattern, out var expandValue))
        {
            try
            {
                states.Add("expand=" + ((ExpandCollapsePattern)expandValue).Current.ExpandCollapseState);
            }
            catch
            {
                // Optional state.
            }
        }

        if (TryPattern(element, TogglePattern.Pattern, out var toggleValue))
        {
            try
            {
                states.Add("toggle=" + ((TogglePattern)toggleValue).Current.ToggleState);
            }
            catch
            {
                // Optional state.
            }
        }

        if (TryPattern(element, SelectionItemPattern.Pattern, out var selectionValue))
        {
            try
            {
                states.Add("selected=" + ((SelectionItemPattern)selectionValue).Current.IsSelected);
            }
            catch
            {
                // Optional state.
            }
        }

        if (TryPattern(element, ValuePattern.Pattern, out var valuePattern))
        {
            try
            {
                states.Add("value=" + Sanitize(((ValuePattern)valuePattern).Current.Value));
            }
            catch
            {
                // Optional value.
            }
        }

        if (TryPattern(element, RangeValuePattern.Pattern, out var rangeValue))
        {
            try
            {
                var range = ((RangeValuePattern)rangeValue).Current;
                states.Add(
                    $"range={range.Value:0.###}/{range.Minimum:0.###}..{range.Maximum:0.###}" +
                    $" small={range.SmallChange:0.###} large={range.LargeChange:0.###}");
            }
            catch
            {
                // Optional range value.
            }
        }
    }

    private static void AddPattern(
        ICollection<string> target,
        AutomationElement element,
        AutomationPattern pattern,
        string name)
    {
        if (TryPattern(element, pattern, out _))
        {
            target.Add(name);
        }
    }

    private static bool TryPattern(AutomationElement element, AutomationPattern pattern, out object value)
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

    private static string ReadStringProperty(AutomationElement element, AutomationProperty property)
    {
        try
        {
            var value = element.GetCurrentPropertyValue(property, ignoreDefaultValue: true);
            return ReferenceEquals(value, AutomationElement.NotSupported) ? string.Empty : value as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeRuntimeId(AutomationElement element)
    {
        try
        {
            return string.Join('.', element.GetRuntimeId());
        }
        catch
        {
            return "?";
        }
    }

    private static void Add(ICollection<string> target, string? value)
    {
        var normalized = Sanitize(value);
        if (!string.IsNullOrWhiteSpace(normalized) && !target.Contains(normalized, StringComparer.Ordinal))
        {
            target.Add(normalized);
        }
    }

    private static string Sanitize(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length <= 240 ? normalized : normalized[..237] + "...";
    }
}