using System.Windows;
using CodexPalette.Native.Models;

namespace CodexPalette.Native.Tests;

public sealed class WindowPlacementTests
{
    [Fact]
    public void OpenBounds_KeepClosedCapsuleAnchorFixed()
    {
        var anchor = new Point(900, 700);
        var bounds = WindowPlacement.GetWindowBounds(true, anchor);
        var restoredAnchor = WindowPlacement.GetAnchorFromWindow(true, bounds.Left, bounds.Top);

        Assert.Equal(anchor, restoredAnchor);
        Assert.Equal(WindowPlacement.OpenWidth, bounds.Width);
        Assert.Equal(WindowPlacement.OpenHeight, bounds.Height);
    }

    [Fact]
    public void SelectorAnchor_MatchesOverlayCapsuleOffset()
    {
        var selector = new Rect(1000, 800, 238, 50);

        Assert.Equal(new Point(1000, 800), WindowPlacement.GetSelectorAnchor(selector));
    }
}
