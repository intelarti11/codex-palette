using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CodexPalette.Native.Models;
using CodexPalette.Native.ViewModels;

namespace CodexPalette.Native;

public partial class MainWindow
{
    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        SetOpen(!_viewModel.IsOpen);
        if (_viewModel.IsOpen)
        {
            await Dispatcher.Yield(DispatcherPriority.Render);
            if (_viewModel.IsMatrixEmpty)
            {
                await _viewModel.DiscoverAsync();
            }
            else
            {
                await _viewModel.RefreshAsync(showErrors: true);
            }
        }
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => SetOpen(false);
    private void Quit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private async void ResetPosition_Click(object sender, RoutedEventArgs e)
    {
        _manualOffset = null;
        _anchor = _selectorAnchor ?? GetCenteredAnchor();
        _viewModel.ClearNotice();
        ApplyWindowBounds();
        await SavePlacementAsync();
    }

    private async void PaletteCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PaletteCellViewModel cell } &&
            await _viewModel.ApplySelectionAsync(cell))
        {
            SetOpen(false);
        }
    }

    private async void SpeedOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SpeedOptionViewModel option })
        {
            await _viewModel.ApplySpeedAsync(option);
        }
    }

    private async void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        _dragging = true;
        try
        {
            DragMove();
            _anchor = WindowPlacement.GetAnchorFromWindow(
                _viewModel.IsOpen, Left, Top, _closedWidth, _closedHeight);
            if (_selectorAnchor is Point selectorAnchor)
            {
                _manualOffset = _anchor - selectorAnchor;
            }
            await SavePlacementAsync();
        }
        catch (InvalidOperationException)
        {
            // The button can be released before WPF starts the drag loop.
        }
        finally
        {
            _dragging = false;
            _tracker.RequestRefresh();
        }
    }
}
