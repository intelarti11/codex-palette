namespace CodexPalette.Native.Models;

public sealed record NativePaletteState(
    IReadOnlyList<string> Models,
    IReadOnlyList<string> Efforts,
    IReadOnlyList<IReadOnlyList<int>> SupportedEfforts,
    string SpeedLabel,
    IReadOnlyList<string> Speeds,
    int ModelIndex,
    int EffortIndex,
    int SpeedIndex,
    string CurrentModel,
    string CurrentEffort,
    string SelectorText);

public sealed record SelectionResult(string DisplayName);

public sealed record SpeedSelectionResult(int Index, string Label);
