using System.Windows;
using System.Windows.Input;
using CodexPalette.Native.Automation;
using CodexPalette.Native.Infrastructure;
using CodexPalette.Native.ViewModels;

namespace CodexPalette.Native;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsStore _settingsStore;
    private readonly CodexAutomationService _automation;
    private readonly CodexWindowTracker _tracker;
    private Point _anchor;
    private Point? _selectorAnchor;
    private Vector? _manualOffset;
    private bool _dragging;

    public MainWindow(MainViewModel viewModel, CodexAutomationService automation, SettingsStore settingsStore)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsStore = settingsStore;
        _automation = automation;
        _automation.PassiveStateChanged += Automation_PassiveStateChanged;
        _tracker = new CodexWindowTracker(automation);
        _tracker.StateChanged += Tracker_StateChanged;
        DataContext = _viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = await _settingsStore.LoadAsync();
        _anchor = settings.LastX is not null && settings.LastY is not null
            ? new Point(settings.LastX.Value, settings.LastY.Value)
            : GetCenteredAnchor();
        if (settings.OffsetX is not null && settings.OffsetY is not null)
        {
            _manualOffset = new Vector(settings.OffsetX.Value, settings.OffsetY.Value);
        }

        ApplyWindowBounds();
        _tracker.Start();
        await _viewModel.RefreshAsync();
    }

    private void Automation_PassiveStateChanged(object? sender, EventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() => _viewModel.RefreshAsync());
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _automation.PassiveStateChanged -= Automation_PassiveStateChanged;
        _tracker.Dispose();
        _automation.Dispose();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel.IsOpen)
        {
            SetOpen(false);
            e.Handled = true;
        }
    }
}
