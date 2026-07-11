using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using CodexPalette.Native.Models;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private static readonly string[] ModelNames = ModelCatalog.All.Select(static model => model.Name).ToArray();

    [GeneratedRegex(@"^5\.(6 (Sol|Terra|Luna)|5|4( Mini)?)\s")]
    private static partial Regex SelectorRegex();

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

    public Task<SelectionResult> ApplySelectionAsync(
        int modelIndex,
        int effortIndex,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ApplySelectionCore(modelIndex, effortIndex, cancellationToken), cancellationToken);

    public Task<SpeedSelectionResult> ApplySpeedAsync(
        int speedIndex,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ApplySpeedCore(speedIndex, cancellationToken), cancellationToken);

    private static Rect? TryGetSelectorBoundsCore(nint mainWindowHandle)
    {
        if (mainWindowHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            var root = AutomationElement.FromHandle(mainWindowHandle);
            var condition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button);
            var buttons = root.FindAll(TreeScope.Descendants, condition);

            for (var index = 0; index < buttons.Count; index++)
            {
                var button = buttons[index];
                if (!SelectorRegex().IsMatch(button.Current.Name ?? string.Empty))
                {
                    continue;
                }

                var bounds = button.Current.BoundingRectangle;
                return bounds.IsEmpty ? null : bounds;
            }
        }
        catch
        {
            // UI Automation can briefly expose stale elements during Codex updates.
        }

        return null;
    }

    private NativePaletteState ReadStateCore(CancellationToken cancellationToken)
    {
        using var process = TryFindCodexProcess() ??
            throw new AutomationUnavailableException("The official Codex window could not be found.");

        AutomationContext? context = null;
        try
        {
            context = GetContext(process.Id, cancellationToken);
            var effortOptions = GetMenuOptions(process.Id, context.EffortMenu, 4, 5, effort: true, cancellationToken);
            var efforts = effortOptions.Labels;
            var modelIndex = Array.FindIndex(ModelNames, model => model == context.Model);
            var effortIndex = FindLabelIndex(efforts, context.Effort);
            CloseSilent(context.Selector);

            var speedLabel = string.Empty;
            IReadOnlyList<string> speeds = Array.Empty<string>();
            var speedIndex = -1;

            try
            {
                context = GetContext(process.Id, cancellationToken);
                var speed = GetSpeed(process.Id, context, cancellationToken);
                speedLabel = speed.Label;
                speeds = speed.Labels;
                speedIndex = speed.SelectedIndex;
                CloseSilent(speed.Owner);
            }
            catch
            {
                CloseSilent(context?.Selector);
            }

            return new NativePaletteState(
                efforts,
                speedLabel,
                speeds,
                Math.Max(modelIndex, 0),
                Math.Max(effortIndex, 0),
                speedIndex);
        }
        finally
        {
            CloseSilent(context?.Selector);
        }
    }

    private SelectionResult ApplySelectionCore(
        int modelIndex,
        int effortIndex,
        CancellationToken cancellationToken)
    {
        if (modelIndex < 0 || modelIndex >= ModelNames.Length || effortIndex < 0 || effortIndex > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(modelIndex), "Invalid selection index.");
        }

        if (!ModelCatalog.Supports(modelIndex, effortIndex))
        {
            throw new InvalidOperationException("This reasoning level is unavailable for the selected model.");
        }

        using var process = TryFindCodexProcess() ??
            throw new AutomationUnavailableException("The official Codex window could not be found.");

        var modelName = ModelNames[modelIndex];
        var context = GetContext(process.Id, cancellationToken);
        OpenSilent(context.ModelMenu, cancellationToken);
        var modelItem = FindElement(
            process.Id,
            ControlType.MenuItem,
            modelName,
            exact: true,
            timeoutMilliseconds: 2500,
            cancellationToken);
        SelectSilent(modelItem, cancellationToken);

        FindElement(
            process.Id,
            ControlType.Button,
            "^" + Regex.Escape(modelName) + @"\s",
            exact: false,
            timeoutMilliseconds: 3500,
            cancellationToken);

        context = GetContext(process.Id, cancellationToken);
        var effortOptions = GetMenuOptions(process.Id, context.EffortMenu, 4, 5, effort: true, cancellationToken);
        if (effortIndex >= effortOptions.Items.Count)
        {
            CloseSilent(context.Selector);
            throw new InvalidOperationException("The requested reasoning level is not exposed by Codex.");
        }

        var effort = effortOptions.Labels[effortIndex];
        SelectSilent(effortOptions.Items[effortIndex], cancellationToken);
        var expected = "^" + Regex.Escape(modelName) + @"\s+" + Regex.Escape(effort) + "$";
        var confirmed = FindElement(
            process.Id,
            ControlType.Button,
            expected,
            exact: false,
            timeoutMilliseconds: 3500,
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

        var context = GetContext(process.Id, cancellationToken);
        var speed = GetSpeed(process.Id, context, cancellationToken);
        var label = speed.Labels[speedIndex];
        SelectSilent(speed.Items[speedIndex], cancellationToken);

        var deadline = DateTime.UtcNow.AddMilliseconds(3500);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var currentContext = GetContext(process.Id, cancellationToken);
                var current = GetSpeed(process.Id, currentContext, cancellationToken);
                var ownerName = TextNormalizer.Normalize(current.Control.Current.Name);
                var confirmed = current.SelectedIndex == speedIndex ||
                    ownerName.EndsWith(label, StringComparison.Ordinal);
                CloseSilent(current.Owner);
                if (confirmed)
                {
                    return new SpeedSelectionResult(speedIndex, label);
                }
            }
            catch
            {
                // Codex may rebuild the popup tree between confirmation attempts.
            }

            Thread.Sleep(100);
        }

        throw new AutomationUnavailableException("Codex did not confirm the requested speed.");
    }


}

public sealed class AutomationUnavailableException(string message) : InvalidOperationException(message);
