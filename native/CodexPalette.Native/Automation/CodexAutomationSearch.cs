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
            foreach (var element in GetElements(processId, controlType))
            {
                var elementName = element.Current.Name ?? string.Empty;
                var matches = exact
                    ? string.Equals(elementName, name, StringComparison.Ordinal)
                    : regex!.IsMatch(elementName);
                if (matches)
                {
                    return element;
                }
            }

            Thread.Sleep(70);
        }

        throw new AutomationUnavailableException($"Codex control not found: {name}");
    }

    private static IReadOnlyList<AutomationElement> GetElements(int processId, ControlType controlType)
    {
        var condition = new PropertyCondition(AutomationElement.ProcessIdProperty, processId);
        var all = AutomationElement.RootElement.FindAll(TreeScope.Descendants, condition);
        var result = new List<AutomationElement>();

        for (var index = 0; index < all.Count; index++)
        {
            try
            {
                var element = all[index];
                if (element.Current.ControlType == controlType)
                {
                    result.Add(element);
                }
            }
            catch
            {
                // Ignore elements invalidated while traversing the tree.
            }
        }

        return result;
    }

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
