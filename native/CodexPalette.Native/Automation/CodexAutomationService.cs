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

        if (!_threadClient.IsLinked)
        {
            throw new AutomationUnavailableException(
                "Liez d’abord le fil actif avec le bouton chaîne. La palette n’ouvrira plus le menu natif automatiquement.");
        }

        using var process = TryFindCodexProcess() ??
            throw new AutomationUnavailableException("The official Codex window could not be found.");

        var modelName = cached.Models[modelIndex];
        var effortName = cached.Efforts[effortIndex];
        var identifiers = ResolveThreadSelectionIds(modelName, effortName, cancellationToken);
        _threadClient.UpdateSettingsAsync(
                identifiers.ModelId,
                identifiers.EffortId,
                updateServiceTier: false,
                serviceTier: null,
                cancellationToken)
            .GetAwaiter()
            .GetResult();

        var selectorText = WaitForPassiveSelection(
            process.Id,
            modelName,
            effortName,
            cancellationToken);
        return new SelectionResult(selectorText);
    }

    private SpeedSelectionResult ApplySpeedCore(int speedIndex, CancellationToken cancellationToken)
    {
        if (speedIndex is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(speedIndex), "Invalid speed index.");
        }

        var cached = GetCachedDiscovery();
        if (cached.Speeds.Count != 2)
        {
            throw new AutomationUnavailableException("The Codex speed catalog is unavailable.");
        }

        if (!_threadClient.IsLinked)
        {
            throw new AutomationUnavailableException(
                "Liez d’abord le fil actif avec le bouton chaîne. La palette n’ouvrira plus le menu natif automatiquement.");
        }

        using var process = TryFindCodexProcess() ??
            throw new AutomationUnavailableException("The official Codex window could not be found.");

        var serviceTier = ResolveThreadServiceTier(speedIndex, cancellationToken);
        _threadClient.UpdateSettingsAsync(
                model: null,
                effort: null,
                updateServiceTier: true,
                serviceTier,
                cancellationToken)
            .GetAwaiter()
            .GetResult();

        Volatile.Write(ref _lastKnownSpeedIndex, speedIndex);
        return new SpeedSelectionResult(speedIndex, cached.Speeds[speedIndex]);
    }
}

public sealed class AutomationUnavailableException(string message) : InvalidOperationException(message);
