using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using CodexPalette.Native.Models;

namespace CodexPalette.Native.Automation;

public sealed partial class CodexAutomationService
{
    private NativePaletteState DiscoverPaletteCore(CancellationToken cancellationToken)
    {
        using var process = TryFindCodexProcess() ??
            throw new AutomationUnavailableException("The official Codex window could not be found.");

        var initial = GetContext(process.Id, cancellationToken);
        var originalModel = initial.Model;
        var originalEffort = initial.Effort;
        var models = initial.ModelOptions.Labels.ToArray();
        CloseContext(initial);

        var labelsByModel = new List<IReadOnlyList<string>>(models.Length);
        try
        {
            foreach (var model in models)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SelectModel(process.Id, model, cancellationToken);
                var context = GetContext(process.Id, cancellationToken);
                try
                {
                    labelsByModel.Add(context.EffortOptions.Labels.ToArray());
                }
                finally
                {
                    CloseContext(context);
                }
            }
        }
        finally
        {
            RestoreSelection(process.Id, originalModel, originalEffort, cancellationToken);
        }

        var efforts = new List<string>();
        foreach (var labels in labelsByModel)
        {
            foreach (var label in labels)
            {
                if (!efforts.Contains(label, StringComparer.Ordinal))
                {
                    efforts.Add(label);
                }
            }
        }

        var supported = labelsByModel
            .Select(labels => (IReadOnlyList<int>)labels
                .Select(label => efforts.FindIndex(value => string.Equals(value, label, StringComparison.Ordinal)))
                .Where(static index => index >= 0)
                .Distinct()
                .Order()
                .ToArray())
            .ToArray();
        UpdateCachedMatrix(models, efforts, supported);

        AutomationContext? speedContext = null;
        SpeedDescriptor? speed = null;
        try
        {
            speedContext = GetContext(process.Id, cancellationToken);
            if (speedContext.SpeedMenu is not null)
            {
                speed = GetSpeed(process.Id, speedContext, cancellationToken);
                UpdateCachedSpeed(speed.Label, speed.Labels);
            }
        }
        catch
        {
            // Speed is optional and must not prevent the matrix from being discovered.
        }
        finally
        {
            CloseSilent(speed?.Control);
            CloseContext(speedContext);
        }

        return ReadStateCore(cancellationToken);
    }

    private void SelectModel(int processId, string model, CancellationToken cancellationToken)
    {
        AutomationContext? context = null;
        try
        {
            context = GetContext(processId, cancellationToken);
            var options = GetMenuOptions(
                processId, context.ModelMenu, 1, 20, false, cancellationToken);
            var index = FindExactLabelIndex(options.Labels, model);
            if (index < 0)
            {
                throw new AutomationUnavailableException($"The model '{model}' is no longer exposed by Codex.");
            }

            SelectSilent(options.Items[index], cancellationToken);
        }
        finally
        {
            CloseContext(context);
        }

        FindElement(
            processId,
            ControlType.Button,
            "^" + Regex.Escape(model) + @"(?:\s|$)",
            exact: false,
            timeoutMilliseconds: 2500,
            cancellationToken);
    }

    private void RestoreSelection(
        int processId,
        string model,
        string effort,
        CancellationToken cancellationToken)
    {
        try
        {
            SelectModel(processId, model, cancellationToken);
            var context = GetContext(processId, cancellationToken);
            try
            {
                var options = GetMenuOptions(
                    processId, context.EffortMenu, 1, 10, true, cancellationToken);
                var index = FindExactLabelIndex(options.Labels, effort);
                if (index >= 0)
                {
                    SelectSilent(options.Items[index], cancellationToken);
                }
            }
            finally
            {
                CloseContext(context);
            }
        }
        catch
        {
            // Best effort: discovery data is still useful if Codex changed during the scan.
        }
    }
}
