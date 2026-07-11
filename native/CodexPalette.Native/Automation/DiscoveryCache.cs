using System.IO;
using System.Text.Json;

namespace CodexPalette.Native.Automation;

internal sealed record DiscoveryCacheData(
    IReadOnlyList<string> Efforts,
    string SpeedLabel,
    IReadOnlyList<string> Speeds)
{
    public static DiscoveryCacheData Empty { get; } = new(Array.Empty<string>(), string.Empty, Array.Empty<string>());
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
            value.Efforts
                .Select(static label => TextNormalizer.Normalize(label, effort: true))
                .Where(static label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.Ordinal)
                .Take(5)
                .ToArray(),
            TextNormalizer.Normalize(value.SpeedLabel),
            value.Speeds
                .Select(static label => TextNormalizer.Normalize(label))
                .Where(static label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToArray());
}
