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
            AutomationEventHandler eventHandler) =>
            System.Windows.Automation.Automation.AddAutomationEventHandler(
                eventId,
                element,
                scope,
                eventHandler);

        public static void RemoveAutomationEventHandler(
            AutomationEvent eventId,
            AutomationElement element,
            AutomationEventHandler eventHandler) =>
            System.Windows.Automation.Automation.RemoveAutomationEventHandler(
                eventId,
                element,
                eventHandler);

        public static void AddStructureChangedEventHandler(
            AutomationElement element,
            TreeScope scope,
            StructureChangedEventHandler eventHandler) =>
            throw new NotSupportedException(
                "StructureChanged subscriptions are unavailable in this UI Automation client build.");

        public static void RemoveStructureChangedEventHandler(
            AutomationElement element,
            StructureChangedEventHandler eventHandler)
        {
            // The corresponding subscription is intentionally unsupported.
        }
    }

    private static class LegacyIAccessiblePattern
    {
        public static AutomationPattern Pattern { get; } = AutomationPattern.LookupById(10018);
    }
}
