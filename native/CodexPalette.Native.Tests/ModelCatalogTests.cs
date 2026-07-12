using CodexPalette.Native.Models;

namespace CodexPalette.Native.Tests;

public sealed class ModelCatalogTests
{
    [Fact]
    public void Create_PreservesNativeNameAndAssignsVisual()
    {
        var model = ModelCatalog.Create("Modelo localizado", 2);

        Assert.Equal("Modelo localizado", model.Name);
        Assert.False(string.IsNullOrWhiteSpace(model.Glyph));
        Assert.StartsWith("#", model.Accent);
    }
}
