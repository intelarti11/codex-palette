using System.Windows;

namespace CodexPalette.Native.Models;

public static class WindowPlacement
{
    public const double GripWidth = 24;
    public const double ClosedWidth = 212;
    public const double ClosedHeight = 50;
    public const double OpenWidth = 680;
    public const double OpenHeight = 360;

    public static Point GetSelectorAnchor(Rect selectorBounds) =>
        new(
            Math.Round(selectorBounds.X - GripWidth),
            Math.Round(selectorBounds.Y));

    public static Rect GetWindowBounds(
        bool isOpen,
        Point anchor,
        double closedWidth = ClosedWidth,
        double closedHeight = ClosedHeight) =>
        isOpen
            ? new Rect(
                anchor.X - (OpenWidth - closedWidth),
                anchor.Y - (OpenHeight - closedHeight),
                OpenWidth,
                OpenHeight)
            : new Rect(anchor.X, anchor.Y, closedWidth, closedHeight);

    public static Point GetAnchorFromWindow(
        bool isOpen,
        double left,
        double top,
        double closedWidth = ClosedWidth,
        double closedHeight = ClosedHeight) =>
        isOpen
            ? new Point(
                left + (OpenWidth - closedWidth),
                top + (OpenHeight - closedHeight))
            : new Point(left, top);
}
