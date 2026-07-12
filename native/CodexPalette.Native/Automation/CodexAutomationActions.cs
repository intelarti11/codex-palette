using System.Windows.Automation;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private static void OpenSilent(AutomationElement element, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetPattern(element, ExpandCollapsePattern.Pattern, out var expandValue))
        {
            throw new AutomationUnavailableException(
                $"The control '{SafeName(element)}' does not expose ExpandCollapsePattern.");
        }

        var pattern = (ExpandCollapsePattern)expandValue;
        var state = pattern.Current.ExpandCollapseState;
        if (state == ExpandCollapseState.LeafNode)
        {
            throw new AutomationUnavailableException(
                $"The control '{SafeName(element)}' is not an expandable popup control.");
        }

        if (state != ExpandCollapseState.Expanded)
        {
            pattern.Expand();
            WaitForExpansionState(element, ExpandCollapseState.Expanded, 500, cancellationToken);
        }
    }

    private static void SelectSilent(AutomationElement element, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryGetPattern(element, SelectionItemPattern.Pattern, out var selectionValue))
        {
            var pattern = (SelectionItemPattern)selectionValue;
            if (!pattern.Current.IsSelected)
            {
                pattern.Select();
            }

            Thread.Sleep(80);
            return;
        }

        if (TryGetPattern(element, TogglePattern.Pattern, out var toggleValue))
        {
            var pattern = (TogglePattern)toggleValue;
            if (pattern.Current.ToggleState != ToggleState.On)
            {
                pattern.Toggle();
            }

            Thread.Sleep(80);
            return;
        }

        if (TryGetPattern(element, InvokePattern.Pattern, out var invokeValue))
        {
            ((InvokePattern)invokeValue).Invoke();
            Thread.Sleep(100);
            return;
        }

        throw new AutomationUnavailableException(
            $"The option '{SafeName(element)}' has no silent UI Automation selection action.");
    }

    private static void SetToggleState(
        AutomationElement element,
        bool enabled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetPattern(element, TogglePattern.Pattern, out var toggleValue))
        {
            throw new AutomationUnavailableException(
                $"The control '{SafeName(element)}' does not expose TogglePattern.");
        }

        var pattern = (TogglePattern)toggleValue;
        var desired = enabled ? ToggleState.On : ToggleState.Off;
        if (pattern.Current.ToggleState != desired)
        {
            pattern.Toggle();
            var deadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (pattern.Current.ToggleState == desired)
                    {
                        break;
                    }
                }
                catch
                {
                    break;
                }

                Thread.Sleep(20);
            }
        }
    }

    private static void CloseSilent(AutomationElement? element)
    {
        if (element is null)
        {
            return;
        }

        try
        {
            if (TryGetPattern(element, ExpandCollapsePattern.Pattern, out var value))
            {
                var pattern = (ExpandCollapsePattern)value;
                if (pattern.Current.ExpandCollapseState == ExpandCollapseState.Expanded)
                {
                    pattern.Collapse();
                    WaitForExpansionState(element, ExpandCollapseState.Collapsed, 350, CancellationToken.None);
                }
            }
        }
        catch
        {
            // Chromium can invalidate a popup element while it is being closed.
        }
    }

    private static void WaitForExpansionState(
        AutomationElement element,
        ExpandCollapseState expected,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (TryGetPattern(element, ExpandCollapsePattern.Pattern, out var value) &&
                    ((ExpandCollapsePattern)value).Current.ExpandCollapseState == expected)
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            Thread.Sleep(20);
        }
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Current.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
