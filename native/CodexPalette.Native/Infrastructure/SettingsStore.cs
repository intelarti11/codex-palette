using System.Text.Json;
using CodexPalette.Native.Models;

namespace CodexPalette.Native.Infrastructure;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexPalette");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "settings.json");
    }

    public async Task<PaletteSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new PaletteSettings();
            }

            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<PaletteSettings>(stream) ?? new PaletteSettings();
        }
        catch
        {
            return new PaletteSettings();
        }
    }

    public async Task SaveAsync(PaletteSettings settings)
    {
        var temporaryPath = _path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
        }

        File.Move(temporaryPath, _path, true);
    }
}
