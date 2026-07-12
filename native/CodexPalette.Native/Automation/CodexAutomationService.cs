using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using CodexPalette.Native.Models;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    [GeneratedRegex(@"^\d+(?:\.\d+)+(?:\s+.+)$")]
    private static partial Regex SelectorRegex();

    [GeneratedRegex(@"^\d+(?:\.\d+)+(?:\s+\S+)*$")]
    private static partial Regex ModelLabelRegex();

    public Process? TryFindCodexProcess()
    {
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            try
            {
                if (process.MainWindowHandle == nint.Zero)
                {
                    process.Dispose();
                    continue;
                }

                var path = process.MainModule?.FileName ?? string.Empty;
                var title = process.MainWindowTitle ?? string.Empty;
                if (path.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Codex", StringComparison.OrdinalIgnoreCase))
                {
                    return process;
                }
            }
            catch
            {
                // Process metadata may be unavailable while the app is restarting.
            }

            process.Dispose();
        }

        return null;
    }

    public Task<Rect?> TryGetSelectorBoundsAsync(nint mainWindowHandle, CancellationToken cancellationToken = default) =>
        Task.Run(() => TryGetSelectorBoundsCore(mainWindowHandle), cancellationToken);

    public Task<NativePaletteState> ReadStateAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadStateCore(cancellationToken), cancellationToken);

    public Task<NativePaletteState> DiscoverPaletteAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => DiscoverPaletteCore(cancellationToken), cancellationToken);

    public Task<SelectionResult> ApplySelectionAsync(
        int modelIndex,
        int effortIndex,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ApplySelectionCore(modelIndex, effortIndex, cancellationToken), cancellationToken);

    public Task<SpeedSelectionResult> ApplySpeedAsync(
        int speedIndex,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ApplySpeedCore(speedIndex, cancellationToken), cancellationToken);

    private Rect? TryGetSelectorBoundsCore(nint mainWindowHandle)
    {
        if (mainWindowHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            var root = AutomationElement.FromHandle(mainWindowHandle);
            var selector = FindSelector(root.Current.ProcessId, 300, CancellationToken.None);
            var bounds = selector.Current.BoundingRectangle;
            return bounds.IsEmpty ? null : bounds;
        }
        catch
        {
            // UI Automation can briefly expose stale elements during Codex updates.
        }

        return null;
    }

    private NativePaletteState ReadStateCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = TryFindCodexProcess() ??
            throw new AutomationUnavailableException("The official Codex window could not be found.");

        if (!TryReadCurrentSelection(process.Id, out var model, out var effort, out var selectorText))
        {
            throw new AutomationUnavailableException("The current Codex model and effort could not be read.");
        }

        // This method never expands or invokes a control. It only inspects the existing tree.
        LearnFromPassiveTree(process.Id, effort);
        var cached = GetCachedDiscovery();
        var modelIndex = cached.Models.ToList().FindIndex(value => value == model);
        var effortIndex = cached.Efforts.Count > 0
            ? FindLabelIndex(cached.Efforts, effort)
            : -1;
        var speedIndex = FindPassiveSpeedIndex(process.Id, cached.Speeds);
        if (speedIndex < 0)
        {
            speedIndex = Volatile.Read(ref _lastKnownSpeedIndex);
        }

        return new NativePaletteState(
            cached.Models,
            cached.Efforts,
            cached.SupportedEfforts,
            cached.SpeedLabel,
            cached.Speeds,
            modelIndex,
            effortIndex,
            speedIndex,
            model,
            effort,
            selectorText);
    }

    private static int FindPassiveSpeedIndex(int processId, IReadOnlyList<string> labels)
    {
        if (labels.Count != 2)
        {
            return -1;
        }

        var trigger = FindTriggerForLabels(processId, labels);
        if (trigger is null)
        {
            return -1;
        }

        var values = GetElementStrings(trigger);
        for (var index = 0; index < labels.Count; index++)
        {
            if (values.Any(value => value.EndsWith(labels[index], StringComparison.Ordinal)))
            {
                return index;
            }
        }

        return -1;
    }

    private SelectionResult ApplySelectionCore(
        int modelIndex,
        int effortIndex,
        CancellationToken cancellationToken)
    {
        var cached = GetCachedDiscovery();
        if (modelIndex < 0 || modelIndex >= cached.Models.Count ||
            effortIndex < 0 || effortIndex >= cached.Efforts.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(modelIndex), "Invalid selection index.");
        }

        if (modelIndex >= cached.SupportedEfforts.Count ||
            !cached.SupportedEfforts[modelIndex].Contains(effortIndex))
        {
            throw new InvalidOperationException("This reasoning level is unavailable for the selected model.");
        }

        using var process = TryFindCodexProcess() ??
            throw new AutomationUnavailableException("The official Codex window could not be found.");

        var modelName = cached.Models[modelIndex];
        var effortName = cached.Efforts[effortIndex];
        AutomationContext? context = null;
        try
        {
            context = GetContext(process.Id, cancellationToken);
            var modelOptions = GetMenuOptions(
                process.Id, context.ModelMenu, 1, 20, false, cancellationToken);
            var targetIndex = FindExactLabelIndex(modelOptions.Labels, modelName);
            if (targetIndex < 0)
            {
                throw new AutomationUnavailableException($"The model '{modelName}' is not exposed by Codex.");
            }

            SelectSilent(modelOptions.Items[targetIndex], cancellationToken);
        }
        finally
        {
            CloseContext(context);
        }

        FindElement(
            process.Id,
            ControlType.Button,
            "^" + Regex.Escape(modelName) + @"\s",
            exact: false,
            timeoutMilliseconds: 2500,
            cancellationToken);

        context = null;
        string effort;
        try
        {
            context = GetContext(process.Id, cancellationToken);
            var effortOptions = GetMenuOptions(
                process.Id, context.EffortMenu, 1, 10, true, cancellationToken);
            var targetIndex = FindExactLabelIndex(effortOptions.Labels, effortName);
            if (targetIndex < 0)
            {
                throw new InvalidOperationException("The requested reasoning level is not exposed by Codex.");
            }

            effort = effortOptions.Labels[targetIndex];
            SelectSilent(effortOptions.Items[targetIndex], cancellationToken);
        }
        finally
        {
            CloseContext(context);
        }

        var expected = "^" + Regex.Escape(modelName) + @"\s+" + Regex.Escape(effort) + "$";
        var confirmed = FindElement(
            process.Id,
            ControlType.Button,
            expected,
            exact: false,
            timeoutMilliseconds: 2500,
            cancellationToken);

        return new SelectionResult(confirmed.Current.Name);
    }

    private SpeedSelectionResult ApplySpeedCore(int speedIndex, CancellationToken cancellationToken)
    {
        if (speedIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(speedIndex), "Invalid speed index.");
        }

        using var process = TryFindCodexProcess() ??
            throw new AutomationUnavailableException("The official Codex window could not be found.");

        AutomationContext? context = null;
        SpeedDescriptor? speed = null;
        try
        {
            context = GetContext(process.Id, cancellationToken);
            speed = GetSpeed(process.Id, context, cancellationToken);
            if (!speed.IsToggle && speedIndex >= speed.Items.Count)
            {
                throw new AutomationUnavailableException("The requested speed is not exposed by Codex.");
            }

            var label = speed.Labels[speedIndex];
            UpdateCachedSpeed(speed.Label, speed.Labels);
            if (speed.IsToggle)
            {
                SetToggleState(speed.Control, speedIndex == 1, cancellationToken);
            }
            else
            {
                SelectSilent(speed.Items[speedIndex], cancellationToken);
            }

            Volatile.Write(ref _lastKnownSpeedIndex, speedIndex);
            return new SpeedSelectionResult(speedIndex, label);
        }
        finally
        {
            CloseSilent(speed?.Control);
            CloseContext(context);
        }
    }

    private static int FindExactLabelIndex(IReadOnlyList<string> labels, string target)
    {
        for (var index = 0; index < labels.Count; index++)
        {
            if (string.Equals(labels[index], target, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}

public sealed class AutomationUnavailableException(string message) : InvalidOperationException(message);
