using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexPalette.Native.Automation;
using CodexPalette.Native.Infrastructure;
using CodexPalette.Native.ViewModels;

namespace CodexPalette.Native;

public sealed class DiagnosticWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly CodexAutomationService _automation;
    private readonly TextBox _logBox;

    public DiagnosticWindow(MainViewModel viewModel, CodexAutomationService automation)
    {
        _viewModel = viewModel;
        _automation = automation;

        Title = "Codex Palette — UI Automation diagnostics";
        Width = 940;
        Height = 620;
        MinWidth = 620;
        MinHeight = 360;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var root = new DockPanel { Margin = new Thickness(10) };

        var description = new TextBlock
        {
            Text = "Journal temporaire de découverte UI Automation. " +
                   "Il est aussi enregistré dans : " + DiagnosticLog.LogPath,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(description, Dock.Top);
        root.Children.Add(description);

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(toolbar, Dock.Top);

        var captureButton = CreateButton("Capturer maintenant");
        captureButton.Click += (_, _) =>
        {
            DiagnosticLog.Write("Manual diagnostic capture requested.");
            _automation.CaptureDiagnosticSnapshot("manual capture");
        };
        toolbar.Children.Add(captureButton);

        var copyButton = CreateButton("Copier tout");
        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(DiagnosticLog.SnapshotText);
                DiagnosticLog.Write("Diagnostic log copied to the clipboard.");
            }
            catch (Exception exception)
            {
                DiagnosticLog.WriteException("Clipboard copy failed", exception);
            }
        };
        toolbar.Children.Add(copyButton);

        var clearButton = CreateButton("Effacer");
        clearButton.Click += (_, _) => DiagnosticLog.Clear();
        toolbar.Children.Add(clearButton);

        root.Children.Add(toolbar);

        _logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
        };
        root.Children.Add(_logBox);

        Content = root;

        DiagnosticLog.EntryAdded += OnEntryAdded;
        DiagnosticLog.Cleared += OnCleared;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += (_, _) =>
        {
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left, workArea.Right - ActualWidth - 24);
            Top = workArea.Top + 24;
            _logBox.Text = DiagnosticLog.SnapshotText;
            _logBox.ScrollToEnd();
        };

        Closed += (_, _) =>
        {
            DiagnosticLog.EntryAdded -= OnEntryAdded;
            DiagnosticLog.Cleared -= OnCleared;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
    }

    private static Button CreateButton(string label) =>
        new()
        {
            Content = label,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 90,
        };

    private void OnEntryAdded(object? sender, string line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _logBox.AppendText(line + Environment.NewLine);
            _logBox.ScrollToEnd();
        });
    }

    private void OnCleared(object? sender, EventArgs args)
    {
        Dispatcher.BeginInvoke(() => _logBox.Clear());
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.Notice) &&
            !string.IsNullOrWhiteSpace(_viewModel.Notice))
        {
            DiagnosticLog.Write("PALETTE NOTICE: " + _viewModel.Notice);
        }
    }
}
