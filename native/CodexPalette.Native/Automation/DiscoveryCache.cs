using System.IO;
using System.Text.Json;

namespace CodexPalette.Native.Automation;

internal sealed record DiscoveryCacheData(
    IReadOnlyList<string> Models,
    IReadOnlyList<string> Efforts,
    IReadOnlyList<IReadOnlyList<int>> SupportedEfforts,
    string SpeedLabel,
    IReadOnlyList<string> Speeds)
{
    public static DiscoveryCacheData Empty { get; } = new(
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<IReadOnlyList<int>>(),
        string.Empty, Array.Empty<string>());
}

internal sealed class DiscoveryCache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public DiscoveryCache()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexPalette");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "discovery-cache.json");
    }

    public DiscoveryCacheData Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return DiscoveryCacheData.Empty;
            }

            var value = JsonSerializer.Deserialize<DiscoveryCacheData>(File.ReadAllText(_path), JsonOptions);
            return value is null ? DiscoveryCacheData.Empty : Sanitize(value);
        }
        catch
        {
            return DiscoveryCacheData.Empty;
        }
    }

    public void Save(DiscoveryCacheData value)
    {
        try
        {
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Sanitize(value), JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch
        {
            // Cache persistence must never affect Codex interaction.
        }
    }

    private static DiscoveryCacheData Sanitize(DiscoveryCacheData value) =>
        new(
            value.Models
                .Select(TextNormalizer.Normalize)
                .Where(static label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.Ordinal)
                .Take(20)
                .ToArray(),
            value.Efforts
                .Select(static label => TextNormalizer.Normalize(label, effort: true))
                .Where(static label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.Ordinal)
                .Take(10)
                .ToArray(),
            value.SupportedEfforts
                .Take(20)
                .Select(static indices => (IReadOnlyList<int>)indices
                    .Where(static index => index >= 0 && index < 10)
                    .Distinct()
                    .Order()
                    .ToArray())
                .ToArray(),
            TextNormalizer.Normalize(value.SpeedLabel),
            value.Speeds
                .Select(static label => TextNormalizer.Normalize(label))
                .Where(static label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToArray());
}
