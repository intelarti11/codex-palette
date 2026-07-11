namespace CodexPalette.Native.Models;

public sealed record NativePaletteState(
    IReadOnlyList<string> Efforts,
    string SpeedLabel,
    IReadOnlyList<string> Speeds,
    int ModelIndex,
    int EffortIndex,
    int SpeedIndex);

public sealed record SelectionResult(string DisplayName);

public sealed record SpeedSelectionResult(int Index, string Label);
