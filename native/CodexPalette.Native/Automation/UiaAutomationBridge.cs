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

        public static void AddStructureChangedEventHandler(
            AutomationElement element,
            TreeScope scope,
            StructureChangedEventHandler handler) =>
            throw new NotSupportedException(
                "StructureChanged subscriptions are unavailable in this UI Automation client build.");

        public static void RemoveStructureChangedEventHandler(
            AutomationElement element,
            StructureChangedEventHandler handler)
        {
            // The corresponding subscription is intentionally unsupported.
        }
    }

    private static class LegacyIAccessiblePattern
    {
        public static AutomationPattern Pattern { get; } = AutomationPattern.LookupById(10018);
    }
}
