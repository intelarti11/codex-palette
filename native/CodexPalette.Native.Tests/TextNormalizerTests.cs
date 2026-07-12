using CodexPalette.Native.Automation;

namespace CodexPalette.Native.Tests;

public sealed class TextNormalizerTests
{
    [Fact]
    public void Normalize_CollapsesWhitespace()
    {
        Assert.Equal("Très élevé", TextNormalizer.Normalize("  Très   élevé  "));
    }

    [Fact]
    public void Normalize_StripsUltraAccessibilityDescription()
    {
        Assert.Equal(
            "Ultra",
            TextNormalizer.Normalize("Ultra Consomme le quota d’utilisation plus vite", effort: true));
    }

    [Fact]
    public void GetGroupLabel_UsesLocalizedIndependentText()
    {
        var label = TextNormalizer.GetGroupLabel(
            "Velocidad Estándar",
            ["Velocidad", "Estándar"],
            ["Estándar", "Rápido"]);

        Assert.Equal("Velocidad", label);
    }

    [Fact]
    public void GetGroupLabel_RemovesSelectedLocalizedSuffix()
    {
        var label = TextNormalizer.GetGroupLabel(
            "Geschwindigkeit Schnell",
            [],
            ["Standard", "Schnell"]);

        Assert.Equal("Geschwindigkeit", label);
    }
}
