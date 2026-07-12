using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using CodexPalette.Native.Infrastructure;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private readonly object _diagnosticGate = new();
    private readonly HashSet<string> _diagnosticRuntimeKeys = new(StringComparer.Ordinal);
    private AutomationEventHandler? _diagnosticAutomationEventHandler;
    private StructureChangedEventHandler? _diagnosticStructureChangedHandler;
    private int _diagnosticsEnabled;
    private long _lastDiagnosticSchedule;

    public void EnableDiagnostics()
    {
        if (Interlocked.Exchange(ref _diagnosticsEnabled, 1) != 0)
        {
            return;
        }

        DiagnosticLog.Write(
            $"Diagnostics enabled. OS={Environment.OSVersion}; " +
            $"UI culture={CultureInfo.CurrentUICulture.Name}; " +
            $"process architecture={RuntimeInformation.ProcessArchitecture}.");

        _diagnosticAutomationEventHandler = DiagnosticAutomationEvent;
        _diagnosticStructureChangedHandler = DiagnosticStructureChanged;

        try
        {
            Automation.AddAutomationEventHandler(
                AutomationElement.MenuOpenedEvent,
                AutomationElement.RootElement,
                TreeScope.Subtree,
                _diagnosticAutomationEventHandler);
            DiagnosticLog.Write("Subscribed to MenuOpenedEvent.");
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Could not subscribe to MenuOpenedEvent", exception);
        }

        try
        {
            Automation.AddAutomationEventHandler(
                WindowPattern.WindowOpenedEvent,
                AutomationElement.RootElement,
                TreeScope.Subtree,
                _diagnosticAutomationEventHandler);
            DiagnosticLog.Write("Subscribed to WindowOpenedEvent.");
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Could not subscribe to WindowOpenedEvent", exception);
        }

        try
        {
            Automation.AddStructureChangedEventHandler(
                AutomationElement.RootElement,
                TreeScope.Subtree,
                _diagnosticStructureChangedHandler);
            DiagnosticLog.Write("Subscribed to StructureChanged events.");
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Could not subscribe to StructureChanged events", exception);
        }

        QueueDiagnosticSnapshot("startup", 0, force: true);
    }

    public void DisableDiagnostics()
    {
        if (Interlocked.Exchange(ref _diagnosticsEnabled, 0) == 0)
        {
            return;
        }

        try
        {
            if (_diagnosticAutomationEventHandler is not null)
            {
                Automation.RemoveAutomationEventHandler(
                    AutomationElement.MenuOpenedEvent,
                    AutomationElement.RootElement,
                    _diagnosticAutomationEventHandler);
                Automation.RemoveAutomationEventHandler(
                    WindowPattern.WindowOpenedEvent,
                    AutomationElement.RootElement,
                    _diagnosticAutomationEventHandler);
            }
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Could not remove diagnostic automation handlers", exception);
        }

        try
        {
            if (_diagnosticStructureChangedHandler is not null)
            {
                Automation.RemoveStructureChangedEventHandler(
                    AutomationElement.RootElement,
                    _diagnosticStructureChangedHandler);
            }
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Could not remove diagnostic structure handler", exception);
        }

        DiagnosticLog.Write("Diagnostics disabled.");
    }

    public void CaptureDiagnosticSnapshot(string reason)
    {
        if (Volatile.Read(ref _diagnosticsEnabled) == 0)
        {
            EnableDiagnostics();
        }

        QueueDiagnosticSnapshot(reason, 0, force: true);
    }

    private void DiagnosticAutomationEvent(object sender, AutomationEventArgs args)
    {
        if (sender is not AutomationElement element ||
            !TryGetDiagnosticProcessId(element, out var processId) ||
            !IsCodexRelatedProcess(processId))
        {
            return;
        }

        DiagnosticLog.Write(
            $"UIA event {args.EventId.ProgrammaticName}: {DescribeDiagnosticElement(element)}");
        QueueDiagnosticSnapshot(args.EventId.ProgrammaticName, 120);
    }

    private void DiagnosticStructureChanged(object sender, StructureChangedEventArgs args)
    {
        if (sender is not AutomationElement element ||
            !TryGetDiagnosticProcessId(element, out var processId) ||
            !IsCodexRelatedProcess(processId))
        {
            return;
        }

        QueueDiagnosticSnapshot("StructureChanged:" + args.StructureChangeType, 140);
    }

    private void QueueDiagnosticSnapshot(string reason, int delayMilliseconds, bool force = false)
    {
        if (Volatile.Read(ref _diagnosticsEnabled) == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        var previous = Interlocked.Read(ref _lastDiagnosticSchedule);
        if (!force && now - previous < 250)
        {
            return;
        }

        Interlocked.Exchange(ref _lastDiagnosticSchedule, now);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (delayMilliseconds > 0)
            {
                Thread.Sleep(delayMilliseconds);
            }

            try
            {
                WriteDiagnosticSnapshot(reason, force);
            }
            catch (Exception exception)
            {
                DiagnosticLog.WriteException("Diagnostic snapshot failed", exception);
            }
        });
    }

    private void WriteDiagnosticSnapshot(string reason, bool includeAll)
    {
        using var mainProcess = TryFindCodexProcess();
        if (mainProcess is null)
        {
            DiagnosticLog.Write($"SNAPSHOT {reason}: no official Codex window found.");
            return;
        }

        AutomationElement? selector = null;
        try
        {
            selector = FindSelector(mainProcess.Id, 600, CancellationToken.None);
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException($"SNAPSHOT {reason}: selector lookup failed", exception);
        }

        var selectorBounds = selector is null ? Rect.Empty : SafeBounds(selector);
        var processIds = GetChatGptProcessIds(mainProcess.Id);
        var elements = new List<AutomationElement>();

        foreach (var processId in processIds)
        {
            elements.AddRange(GetElements(processId));
        }

        if (!selectorBounds.IsEmpty)
        {
            elements.AddRange(GetGlobalInteractiveElementsNear(selectorBounds));
        }

        var unique = new Dictionary<string, AutomationElement>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            if (!TestVisible(element) || !IsDiagnosticInteresting(element, selectorBounds))
            {
                continue;
            }

            var key = GetRuntimeKey(element) ?? BuildFallbackDiagnosticKey(element);
            unique.TryAdd(key, element);
        }

        var ordered = unique
            .Select(pair => new DiagnosticElement(pair.Key, pair.Value, SafeBounds(pair.Value)))
            .OrderBy(item => item.Bounds.Y)
            .ThenBy(item => item.Bounds.X)
            .Take(140)
            .ToArray();

        lock (_diagnosticGate)
        {
            var newItems = ordered
                .Where(item => !_diagnosticRuntimeKeys.Contains(item.Key))
                .ToArray();
            var itemsToWrite = includeAll || _diagnosticRuntimeKeys.Count == 0
                ? ordered
                : newItems;

            DiagnosticLog.Write(
                $"SNAPSHOT {reason}: mainPid={mainProcess.Id}; " +
                $"ChatGPT PIDs=[{string.Join(",", processIds)}]; " +
                $"selector={(selector is null ? "missing" : DescribeDiagnosticElement(selector))}; " +
                $"interactive={ordered.Length}; new={newItems.Length}; writing={itemsToWrite.Length}.");

            foreach (var item in itemsToWrite)
            {
                var prefix = _diagnosticRuntimeKeys.Contains(item.Key) ? "KNOWN" : "NEW";
                DiagnosticLog.Write($"  {prefix} {DescribeDiagnosticElement(item.Element)}");
            }

            _diagnosticRuntimeKeys.Clear();
            foreach (var item in ordered)
            {
                _diagnosticRuntimeKeys.Add(item.Key);
            }
        }
    }

    private static IReadOnlyList<int> GetChatGptProcessIds(int mainProcessId)
    {
        var result = new HashSet<int> { mainProcessId };
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            try
            {
                result.Add(process.Id);
            }
            finally
            {
                process.Dispose();
            }
        }

        return result.Order().ToArray();
    }

    private static IReadOnlyList<AutomationElement> GetGlobalInteractiveElementsNear(Rect selectorBounds)
    {
        try
        {
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Group),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Pane),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Custom),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
            var nodes = AutomationElement.RootElement.FindAll(TreeScope.Descendants, condition);
            var result = new List<AutomationElement>();

            for (var index = 0; index < nodes.Count; index++)
            {
                var element = nodes[index];
                if (TestVisible(element) && IsNearDiagnosticArea(SafeBounds(element), selectorBounds))
                {
                    result.Add(element);
                }
            }

            return result;
        }
        catch
        {
            return Array.Empty<AutomationElement>();
        }
    }

    private static bool IsDiagnosticInteresting(AutomationElement element, Rect selectorBounds)
    {
        var patterns = GetDiagnosticPatternNames(element);
        if (patterns.Count == 0)
        {
            return false;
        }

        if (selectorBounds.IsEmpty)
        {
            return true;
        }

        return IsNearDiagnosticArea(SafeBounds(element), selectorBounds);
    }

    private static bool IsNearDiagnosticArea(Rect candidate, Rect selectorBounds)
    {
        if (candidate.IsEmpty || selectorBounds.IsEmpty)
        {
            return false;
        }

        var expanded = new Rect(
            selectorBounds.X - 900,
            selectorBounds.Y - 720,
            selectorBounds.Width + 1800,
            selectorBounds.Height + 1440);
        return expanded.IntersectsWith(candidate);
    }

    private static string DescribeDiagnosticElement(AutomationElement element)
    {
        var processId = TryGetDiagnosticProcessId(element, out var value) ? value : -1;
        var processName = GetDiagnosticProcessName(processId);
        var bounds = SafeBounds(element);
        var runtime = GetRuntimeKey(element) ?? "?";
        var controlType = GetDiagnosticControlType(element);
        var automationId = SanitizeDiagnosticValue(GetAutomationId(element));
        var className = SanitizeDiagnosticValue(
            GetStringProperty(element, AutomationElement.ClassNameProperty));
        var name = GetDiagnosticDisplayName(element, controlType);
        var patterns = string.Join(",", GetDiagnosticPatternNames(element));

        return $"pid={processId}/{processName} type={controlType} " +
               $"name=\"{name}\" id=\"{automationId}\" class=\"{className}\" " +
               $"patterns=[{patterns}] bounds={bounds.X:0},{bounds.Y:0},{bounds.Width:0},{bounds.Height:0} " +
               $"runtime={runtime}";
    }

    private static IReadOnlyList<string> GetDiagnosticPatternNames(AutomationElement element)
    {
        var result = new List<string>();
        if (TryGetPattern(element, ExpandCollapsePattern.Pattern, out _))
        {
            result.Add("ExpandCollapse");
        }
        if (TryGetPattern(element, InvokePattern.Pattern, out _))
        {
            result.Add("Invoke");
        }
        if (TryGetPattern(element, SelectionItemPattern.Pattern, out _))
        {
            result.Add("SelectionItem");
        }
        if (TryGetPattern(element, SelectionPattern.Pattern, out _))
        {
            result.Add("Selection");
        }
        if (TryGetPattern(element, TogglePattern.Pattern, out _))
        {
            result.Add("Toggle");
        }
        if (TryGetPattern(element, ValuePattern.Pattern, out _))
        {
            result.Add("Value");
        }
        if (TryGetPattern(element, LegacyIAccessiblePattern.Pattern, out _))
        {
            result.Add("LegacyIAccessible");
        }

        return result;
    }

    private static string GetDiagnosticDisplayName(AutomationElement element, string controlType)
    {
        IEnumerable<string> values = controlType is "Button" or "MenuItem" or "ListItem" or
            "RadioButton" or "CheckBox" or "ComboBox"
            ? GetElementStrings(element)
            : new[] { SafeName(element) };

        return SanitizeDiagnosticValue(string.Join(" | ", values.Take(3)));
    }

    private static string GetDiagnosticControlType(AutomationElement element)
    {
        try
        {
            return element.Current.ControlType.ProgrammaticName.Replace("ControlType.", string.Empty);
        }
        catch
        {
            return "?";
        }
    }

    private static bool TryGetDiagnosticProcessId(AutomationElement element, out int processId)
    {
        try
        {
            processId = element.Current.ProcessId;
            return processId > 0;
        }
        catch
        {
            processId = -1;
            return false;
        }
    }

    private static bool IsCodexRelatedProcess(int processId)
    {
        if (IsCodexProcess(processId))
        {
            return true;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return string.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GetDiagnosticProcessName(int processId)
    {
        if (processId <= 0)
        {
            return "?";
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return "?";
        }
    }

    private static string BuildFallbackDiagnosticKey(AutomationElement element)
    {
        var bounds = SafeBounds(element);
        var processId = TryGetDiagnosticProcessId(element, out var value) ? value : -1;
        return $"{processId}:{GetDiagnosticControlType(element)}:{bounds.X:0}:{bounds.Y:0}:" +
               SanitizeDiagnosticValue(SafeName(element));
    }

    private static string SanitizeDiagnosticValue(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length <= 180 ? normalized : normalized[..177] + "...";
    }

    private sealed record DiagnosticElement(string Key, AutomationElement Element, Rect Bounds);
}
