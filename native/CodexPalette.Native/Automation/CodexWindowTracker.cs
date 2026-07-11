using System.Diagnostics;
using System.Windows;
using CodexPalette.Native.Infrastructure;

namespace CodexPalette.Native.Automation;

public sealed record CodexWindowState(
    bool Found,
    bool Visible,
    Rect? SelectorBounds,
    int ProcessId,
    nint MainWindowHandle);

public sealed class CodexWindowTracker : IDisposable
{
    private readonly CodexAutomationService _automation;
    private readonly NativeMethods.WinEventDelegate _winEventCallback;
    private readonly List<nint> _hooks = [];
    private Timer? _fallbackTimer;
    private int _refreshing;
    private bool _disposed;
    private CodexWindowState? _lastState;

    public CodexWindowTracker(CodexAutomationService automation)
    {
        _automation = automation;
        _winEventCallback = OnWinEvent;
    }

    public event EventHandler<CodexWindowState>? StateChanged;

    public void Start()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(CodexWindowTracker));
        }

        AddHook(NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground);
        AddHook(NativeMethods.EventObjectDestroy, NativeMethods.EventObjectLocationChange);
        _fallbackTimer = new Timer(
            static state => ((CodexWindowTracker)state!).RequestRefresh(),
            this,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(2));
    }

    public void RequestRefresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            using var process = _automation.TryFindCodexProcess();
            if (process is null)
            {
                Publish(new CodexWindowState(false, false, null, 0, nint.Zero));
                return;
            }

            var foreground = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundProcessId);
            var currentProcessId = (uint)Environment.ProcessId;
            var visible = foregroundProcessId == (uint)process.Id || foregroundProcessId == currentProcessId;
            var bounds = await _automation.TryGetSelectorBoundsAsync(process.MainWindowHandle).ConfigureAwait(false);
            Publish(new CodexWindowState(
                true,
                visible,
                bounds,
                process.Id,
                process.MainWindowHandle));
        }
        catch
        {
            Publish(new CodexWindowState(false, false, null, 0, nint.Zero));
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void Publish(CodexWindowState state)
    {
        if (_lastState == state)
        {
            return;
        }

        _lastState = state;
        StateChanged?.Invoke(this, state);
    }

    private void AddHook(uint minimumEvent, uint maximumEvent)
    {
        var hook = NativeMethods.SetWinEventHook(
            minimumEvent,
            maximumEvent,
            nint.Zero,
            _winEventCallback,
            0,
            0,
            NativeMethods.WineventOutOfContext);
        if (hook != nint.Zero)
        {
            _hooks.Add(hook);
        }
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime) => RequestRefresh();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fallbackTimer?.Dispose();
        _fallbackTimer = null;
        foreach (var hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
        }
        _hooks.Clear();
    }
}
