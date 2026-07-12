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
    private DiagnosticWindow? _diagnosticWindow;

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

        DiagnosticLog.Write("Codex Palette native application starting.");
        _automation.EnableDiagnostics();

        window.Show();

        _diagnosticWindow = new DiagnosticWindow(viewModel, _automation)
        {
            Owner = window,
        };
        _diagnosticWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _diagnosticWindow?.Close();
        _automation?.DisableDiagnostics();
        _automation?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
