namespace CodexPalette.Native.Models;

public sealed record ModelDefinition(
    string Name,
    string Glyph,
    string Accent,
    IReadOnlySet<int> SupportedEffortIndices);

public static class ModelCatalog
{
    public static IReadOnlyList<ModelDefinition> All { get; } =
    [
        new("5.6 Sol", "☀", "#E6A63A", new HashSet<int> { 0, 1, 2, 3, 4 }),
        new("5.6 Terra", "◆", "#5C9C68", new HashSet<int> { 0, 1, 2, 3, 4 }),
        new("5.6 Luna", "☾", "#7772B8", new HashSet<int> { 0, 1, 2, 3 }),
        new("5.5", "◉", "#4A84AE", new HashSet<int> { 0, 1, 2, 3 }),
        new("5.4", "◇", "#9B6FAE", new HashSet<int> { 0, 1, 2, 3 }),
        new("5.4 Mini", "·", "#777777", new HashSet<int> { 0, 1, 2, 3 }),
    ];

    public static bool Supports(int modelIndex, int effortIndex) =>
        modelIndex >= 0 &&
        modelIndex < All.Count &&
        All[modelIndex].SupportedEffortIndices.Contains(effortIndex);
}
