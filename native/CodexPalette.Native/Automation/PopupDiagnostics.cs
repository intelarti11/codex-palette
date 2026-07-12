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

        if (TryPattern(element, ValuePattern.Pattern, out var valuePattern))
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

        var patterns = new List<string>();
        AddPattern(patterns, element, ExpandCollapsePattern.Pattern, "ExpandCollapse");
        AddPattern(patterns, element, InvokePattern.Pattern, "Invoke");
        AddPattern(patterns, element, SelectionItemPattern.Pattern, "SelectionItem");
        AddPattern(patterns, element, SelectionPattern.Pattern, "Selection");
        AddPattern(patterns, element, TogglePattern.Pattern, "Toggle");
        AddPattern(patterns, element, ValuePattern.Pattern, "Value");
        AddPattern(patterns, element, LegacyIAccessiblePattern.Pattern, "LegacyIAccessible");

        var bounds = current.BoundingRectangle;
        var runtime = SafeRuntimeId(element);
        return $"pid={current.ProcessId} type={current.ControlType.ProgrammaticName} " +
               $"name=\"{Sanitize(string.Join(" | ", values))}\" " +
               $"id=\"{Sanitize(current.AutomationId)}\" class=\"{Sanitize(current.ClassName)}\" " +
               $"patterns=[{string.Join(",", patterns)}] " +
               $"enabled={current.IsEnabled} offscreen={current.IsOffscreen} " +
               $"bounds={bounds.X:0},{bounds.Y:0},{bounds.Width:0},{bounds.Height:0} runtime={runtime}";
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
