using System.Windows;
using System.Windows.Threading;
using CodexPalette.Native.Automation;
using CodexPalette.Native.Models;

namespace CodexPalette.Native;

public partial class MainWindow
{
    private void Tracker_StateChanged(object? sender, CodexWindowState state)
    {
        _ = Dispatcher.InvokeAsync(() => HandleTrackerStateAsync(state), DispatcherPriority.Normal);
    }

    private async Task HandleTrackerStateAsync(CodexWindowState state)
    {
        if (state.SelectorBounds is Rect selectorBounds)
        {
            _selectorAnchor = WindowPlacement.GetSelectorAnchor(selectorBounds);
            if (!_dragging)
            {
                _anchor = _selectorAnchor.Value + (_manualOffset ?? new Vector());
                ApplyWindowBounds();
            }
        }

        if (!state.Visible)
        {
            Opacity = 0;
            Hide();
            return;
        }

        if (!IsVisible)
        {
            Show();
        }
        Opacity = 1;
        Topmost = true;

        if (DateTime.UtcNow - _lastNativeRefresh > TimeSpan.FromSeconds(3))
        {
            _lastNativeRefresh = DateTime.UtcNow;
            await _viewModel.RefreshAsync();
        }
    }

    private void SetOpen(bool open)
    {
        _viewModel.IsOpen = open;
        ApplyWindowBounds();
    }

    private void ApplyWindowBounds()
    {
        var bounds = WindowPlacement.GetWindowBounds(_viewModel.IsOpen, _anchor);
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private Point GetCenteredAnchor()
    {
        var workArea = SystemParameters.WorkArea;
        return new Point(
            workArea.Left + (workArea.Width - WindowPlacement.ClosedWidth) / 2,
            workArea.Top + (workArea.Height - WindowPlacement.ClosedHeight) / 2);
    }

    private Task SavePlacementAsync() =>
        _settingsStore.SaveAsync(new PaletteSettings(
            _anchor.X,
            _anchor.Y,
            _manualOffset?.X,
            _manualOffset?.Y));
}
