using System.Windows;
using CodexPalette.Native.Models;

namespace CodexPalette.Native.Tests;

public sealed class WindowPlacementTests
{
    [Fact]
    public void OpenBounds_KeepClosedCapsuleAnchorFixed()
    {
        var anchor = new Point(900, 700);
        var bounds = WindowPlacement.GetWindowBounds(true, anchor, 260, 44);
        var restoredAnchor = WindowPlacement.GetAnchorFromWindow(true, bounds.Left, bounds.Top, 260, 44);

        Assert.Equal(anchor, restoredAnchor);
        Assert.Equal(WindowPlacement.OpenWidth, bounds.Width);
        Assert.Equal(WindowPlacement.OpenHeight, bounds.Height);
    }

    [Fact]
    public void SelectorAnchor_MatchesOverlayCapsuleOffset()
    {
        var selector = new Rect(1000, 800, 238, 50);

        Assert.Equal(
            new Point(1000 - WindowPlacement.GripWidth, 800),
            WindowPlacement.GetSelectorAnchor(selector));

        var bounds = WindowPlacement.GetWindowBounds(
            false,
            WindowPlacement.GetSelectorAnchor(selector),
            selector.Width + WindowPlacement.GripWidth,
            selector.Height);
        Assert.Equal(selector.X, bounds.X + WindowPlacement.GripWidth);
        Assert.Equal(selector.Width, bounds.Width - WindowPlacement.GripWidth);
    }
}
