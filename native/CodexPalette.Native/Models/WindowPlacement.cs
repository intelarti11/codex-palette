using System.Windows;

namespace CodexPalette.Native.Models;

public static class WindowPlacement
{
    public const double ClosedWidth = 212;
    public const double ClosedHeight = 50;
    public const double OpenWidth = 680;
    public const double OpenHeight = 360;

    public static Point GetSelectorAnchor(Rect selectorBounds) =>
        new(
            Math.Round(selectorBounds.X + selectorBounds.Width / 2 - 119),
            Math.Round(selectorBounds.Y + selectorBounds.Height / 2 - 25));

    public static Rect GetWindowBounds(bool isOpen, Point anchor) =>
        isOpen
            ? new Rect(
                anchor.X - (OpenWidth - ClosedWidth),
                anchor.Y - (OpenHeight - ClosedHeight),
                OpenWidth,
                OpenHeight)
            : new Rect(anchor.X, anchor.Y, ClosedWidth, ClosedHeight);

    public static Point GetAnchorFromWindow(bool isOpen, double left, double top) =>
        isOpen
            ? new Point(
                left + (OpenWidth - ClosedWidth),
                top + (OpenHeight - ClosedHeight))
            : new Point(left, top);
}
