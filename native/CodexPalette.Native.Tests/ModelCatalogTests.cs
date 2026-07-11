using CodexPalette.Native.Models;

namespace CodexPalette.Native.Tests;

public sealed class ModelCatalogTests
{
    [Theory]
    [InlineData(0, 4, true)]
    [InlineData(1, 4, true)]
    [InlineData(2, 4, false)]
    [InlineData(5, 3, true)]
    public void Supports_MatchesCodexMatrix(int modelIndex, int effortIndex, bool expected)
    {
        Assert.Equal(expected, ModelCatalog.Supports(modelIndex, effortIndex));
    }
}
