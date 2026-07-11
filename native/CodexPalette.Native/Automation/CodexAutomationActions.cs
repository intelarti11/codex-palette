using System.Windows.Automation;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private static void OpenSilent(AutomationElement element, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryGetPattern(element, ExpandCollapsePattern.Pattern, out var expandValue))
        {
            var pattern = (ExpandCollapsePattern)expandValue;
            var state = pattern.Current.ExpandCollapseState;
            if (state != ExpandCollapseState.LeafNode)
            {
                if (state != ExpandCollapseState.Expanded)
                {
                    pattern.Expand();
                }

                Thread.Sleep(240);
                return;
            }
        }

        if (TryGetPattern(element, InvokePattern.Pattern, out var invokeValue))
        {
            ((InvokePattern)invokeValue).Invoke();
            Thread.Sleep(240);
            return;
        }

        throw new AutomationUnavailableException(
            $"The control '{element.Current.Name}' has no silent UI Automation action.");
    }

    private static void SelectSilent(AutomationElement element, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryGetPattern(element, InvokePattern.Pattern, out var invokeValue))
        {
            ((InvokePattern)invokeValue).Invoke();
            Thread.Sleep(300);
            return;
        }

        if (TryGetPattern(element, SelectionItemPattern.Pattern, out var selectionValue))
        {
            ((SelectionItemPattern)selectionValue).Select();
            Thread.Sleep(300);
            return;
        }

        throw new AutomationUnavailableException(
            $"The option '{element.Current.Name}' has no silent UI Automation action.");
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
                    Thread.Sleep(150);
                }
            }
        }
        catch
        {
            // A popup can disappear before cleanup runs.
        }
    }
}
