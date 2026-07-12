using System.Threading;
using System.Windows;
using CodexPalette.Native.Automation;
using CodexPalette.Native.Infrastructure;
using CodexPalette.Native.ViewModels;

namespace CodexPalette.Native;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private CodexAutomationService? _automation;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "CodexPalette.Native.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        _automation = new CodexAutomationService();
        var settings = new SettingsStore();
        var viewModel = new MainViewModel(_automation);
        var window = new MainWindow(viewModel, _automation, settings);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _automation?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
