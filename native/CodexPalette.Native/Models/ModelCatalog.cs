namespace CodexPalette.Native.Models;

public sealed record ModelDefinition(string Name, string Glyph, string Accent);

public static class ModelCatalog
{
    private static readonly (string Glyph, string Accent)[] Visuals =
    [
        ("☀", "#E6A63A"),
        ("◆", "#5C9C68"),
        ("☾", "#7772B8"),
        ("◉", "#4A84AE"),
        ("◇", "#9B6FAE"),
        ("·", "#777777"),
    ];

    public static ModelDefinition Create(string name, int index)
    {
        var visual = Visuals[Math.Abs(index) % Visuals.Length];
        return new ModelDefinition(name, visual.Glyph, visual.Accent);
    }
}
