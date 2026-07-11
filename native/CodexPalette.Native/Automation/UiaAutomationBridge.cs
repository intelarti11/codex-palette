using System.Windows.Automation;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private static class Automation
    {
        public static void AddAutomationEventHandler(
            AutomationEvent eventId,
            AutomationElement element,
            TreeScope scope,
            AutomationEventHandler handler) =>
            global::System.Windows.Automation.Automation.AddAutomationEventHandler(
                eventId,
                element,
                scope,
                handler);

        public static void RemoveAutomationEventHandler(
            AutomationEvent eventId,
            AutomationElement element,
            AutomationEventHandler handler) =>
            global::System.Windows.Automation.Automation.RemoveAutomationEventHandler(
                eventId,
                element,
                handler);
    }
}
