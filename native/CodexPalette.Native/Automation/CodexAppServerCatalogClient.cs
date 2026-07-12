using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CodexPalette.Native.Automation;

internal sealed record CodexCatalogEffort(string Id, string DisplayName);
internal sealed record CodexCatalogServiceTier(string Id, string DisplayName);
internal sealed record CodexCatalogModel(
    string Id,
    string DisplayName,
    IReadOnlyList<CodexCatalogEffort> Efforts,
    IReadOnlyList<CodexCatalogServiceTier> ServiceTiers);
internal sealed record CodexCatalog(
    IReadOnlyList<CodexCatalogModel> Models,
    string Source,
    string? CodexHome);

internal sealed class CodexAppServerCatalogClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public CodexCatalog Load(Process? desktopProcess, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        string? discoveredCodexHome = null;

        foreach (var executable in EnumerateExecutableCandidates(desktopProcess))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var catalog = LoadFromAppServerAsync(executable, cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                if (catalog.Models.Count > 0)
                {
                    return catalog;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (CodexAppServerException exception)
            {
                discoveredCodexHome ??= exception.CodexHome;
                errors.Add($"{Path.GetFileName(executable)}: {exception.Message}");
            }
            catch (Exception exception)
            {
                errors.Add($"{Path.GetFileName(executable)}: {exception.Message}");
            }
        }

        foreach (var cachePath in EnumerateCacheCandidates(discoveredCodexHome))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var catalog = LoadFromCache(cachePath);
                if (catalog.Models.Count > 0)
                {
                    return catalog;
                }
            }
            catch (Exception exception)
            {
                errors.Add($"{cachePath}: {exception.Message}");
            }
        }

        var detail = errors.Count == 0
            ? "No Codex app-server executable or models cache could be located."
            : string.Join(" | ", errors.Take(4));
        throw new AutomationUnavailableException(
            "The Codex model catalog could not be read without opening the selector. " + detail);
    }

    private static async Task<CodexCatalog> LoadFromAppServerAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        var token = timeout.Token;

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "app-server --stdio",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("the process did not start");
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("app-server could not be started", exception);
        }

        var stderrTask = process.StandardError.ReadToEndAsync(token);
        string? codexHome = null;
        try
        {
            await WriteMessageAsync(
                process.StandardInput,
                new
                {
                    method = "initialize",
                    id = 1,
                    @params = new
                    {
                        clientInfo = new
                        {
                            name = "codex_palette",
                            title = "Codex Palette",
                            version = "0.6.0",
                        },
                    },
                },
                token).ConfigureAwait(false);

            var initialize = await ReadResponseAsync(process.StandardOutput, 1, token).ConfigureAwait(false);
            codexHome = GetString(initialize, "codexHome", "codex_home");

            await WriteMessageAsync(
                process.StandardInput,
                new { method = "initialized", @params = new { } },
                token).ConfigureAwait(false);

            var models = new List<CodexCatalogModel>();
            string? cursor = null;
            var requestId = 2;
            do
            {
                await WriteMessageAsync(
                    process.StandardInput,
                    new
                    {
                        method = "model/list",
                        id = requestId,
                        @params = new
                        {
                            cursor,
                            limit = 100,
                            includeHidden = false,
                        },
                    },
                    token).ConfigureAwait(false);

                var result = await ReadResponseAsync(process.StandardOutput, requestId, token)
                    .ConfigureAwait(false);
                if (TryGetProperty(result, out var data, "data") && data.ValueKind == JsonValueKind.Array)
                {
                    models.AddRange(ParseModels(data, rawCache: false));
                }

                cursor = GetString(result, "nextCursor", "next_cursor");
                requestId++;
            }
            while (!string.IsNullOrWhiteSpace(cursor));

            return new CodexCatalog(
                DeduplicateModels(models),
                "app-server",
                codexHome);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CodexAppServerException("app-server timed out", codexHome);
        }
        catch (CodexAppServerException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new CodexAppServerException(exception.Message, codexHome, exception);
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The short-lived catalog process may already have exited.
            }

            try
            {
                _ = await stderrTask.ConfigureAwait(false);
            }
            catch
            {
                // stderr is diagnostic only.
            }
        }
    }

    private static async Task WriteMessageAsync(
        StreamWriter writer,
        object message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteLineAsync(JsonSerializer.Serialize(message)).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonElement> ReadResponseAsync(
        StreamReader reader,
        int expectedId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new CodexAppServerException("app-server closed its output stream");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (!TryGetProperty(root, out var id, "id") ||
                    id.ValueKind != JsonValueKind.Number ||
                    !id.TryGetInt32(out var value) ||
                    value != expectedId)
                {
                    continue;
                }

                if (TryGetProperty(root, out var error, "error"))
                {
                    var message = GetString(error, "message") ?? error.GetRawText();
                    throw new CodexAppServerException(message);
                }

                if (!TryGetProperty(root, out var result, "result"))
                {
                    throw new CodexAppServerException("app-server response did not contain a result");
                }

                return result.Clone();
            }
        }
    }

    private static CodexCatalog LoadFromCache(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!TryGetProperty(document.RootElement, out var models, "models") ||
            models.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("models_cache.json does not contain a models array");
        }

        return new CodexCatalog(
            DeduplicateModels(ParseModels(models, rawCache: true)),
            "models_cache.json",
            Path.GetDirectoryName(path));
    }

    private static IReadOnlyList<CodexCatalogModel> ParseModels(JsonElement array, bool rawCache)
    {
        var result = new List<CodexCatalogModel>();
        foreach (var element in array.EnumerateArray())
        {
            if (rawCache)
            {
                var visibility = GetString(element, "visibility");
                if (!string.IsNullOrWhiteSpace(visibility) &&
                    !string.Equals(visibility, "list", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            else if (TryGetProperty(element, out var hidden, "hidden") &&
                     hidden.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            var id = GetString(element, "id", "model", "slug");
            var displayName = GetString(element, "displayName", "display_name");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            var efforts = ParseEfforts(element);
            if (efforts.Count == 0)
            {
                var defaultEffort = GetString(
                    element,
                    "defaultReasoningEffort",
                    "default_reasoning_effort",
                    "default_reasoning_level");
                if (!string.IsNullOrWhiteSpace(defaultEffort))
                {
                    efforts.Add(new CodexCatalogEffort(defaultEffort, EffortDisplayName(defaultEffort)));
                }
            }

            if (efforts.Count == 0)
            {
                continue;
            }

            result.Add(new CodexCatalogModel(
                id,
                TextNormalizer.Normalize(displayName),
                efforts,
                ParseServiceTiers(element)));
        }

        return result;
    }

    private static List<CodexCatalogEffort> ParseEfforts(JsonElement model)
    {
        var result = new List<CodexCatalogEffort>();
        if (!TryGetProperty(
                model,
                out var efforts,
                "supportedReasoningEfforts",
                "supported_reasoning_efforts",
                "supported_reasoning_levels") ||
            efforts.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in efforts.EnumerateArray())
        {
            var id = option.ValueKind == JsonValueKind.String
                ? option.GetString()
                : GetString(option, "reasoningEffort", "reasoning_effort", "effort");
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
            {
                continue;
            }

            result.Add(new CodexCatalogEffort(id, EffortDisplayName(id)));
        }

        return result;
    }

    private static IReadOnlyList<CodexCatalogServiceTier> ParseServiceTiers(JsonElement model)
    {
        var result = new List<CodexCatalogServiceTier>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TryGetProperty(model, out var tiers, "serviceTiers", "service_tiers") &&
            tiers.ValueKind == JsonValueKind.Array)
        {
            foreach (var tier in tiers.EnumerateArray())
            {
                var id = GetString(tier, "id");
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                {
                    continue;
                }

                var name = GetString(tier, "name");
                result.Add(new CodexCatalogServiceTier(id, TierDisplayName(id, name)));
            }
        }

        if (TryGetProperty(model, out var legacy, "additionalSpeedTiers", "additional_speed_tiers") &&
            legacy.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in legacy.EnumerateArray())
            {
                var id = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                {
                    continue;
                }

                result.Add(new CodexCatalogServiceTier(id, TierDisplayName(id, null)));
            }
        }

        if (result.Count > 0 && !ids.Contains("standard") && !ids.Contains("default"))
        {
            result.Insert(0, new CodexCatalogServiceTier("standard", TierDisplayName("standard", null)));
        }

        return result;
    }

    private static IReadOnlyList<CodexCatalogModel> DeduplicateModels(IEnumerable<CodexCatalogModel> models)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.Ordinal);
        return models
            .Where(model => ids.Add(model.Id) && names.Add(model.DisplayName))
            .Take(20)
            .ToArray();
    }

    internal static string EffortDisplayName(string id)
    {
        var french = string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            "fr",
            StringComparison.OrdinalIgnoreCase);
        return id.ToLowerInvariant() switch
        {
            "none" => french ? "Aucun" : "None",
            "minimal" => "Minimal",
            "low" => french ? "Léger" : "Low",
            "medium" => french ? "Standard" : "Medium",
            "high" => french ? "Élevé" : "High",
            "xhigh" => french ? "Très élevé" : "Extra high",
            "max" => french ? "Maximum" : "Max",
            "ultra" => "Ultra",
            _ => HumanizeIdentifier(id),
        };
    }

    internal static string TierDisplayName(string id, string? serverName)
    {
        var french = string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            "fr",
            StringComparison.OrdinalIgnoreCase);
        return id.ToLowerInvariant() switch
        {
            "standard" or "default" => "Standard",
            "fast" => french ? "Rapide" : "Fast",
            _ when !string.IsNullOrWhiteSpace(serverName) => TextNormalizer.Normalize(serverName),
            _ => HumanizeIdentifier(id),
        };
    }

    private static string HumanizeIdentifier(string value)
    {
        var words = value.Replace('_', ' ').Replace('-', ' ').Trim();
        if (words.Length == 0)
        {
            return value;
        }

        return char.ToUpper(words[0], CultureInfo.CurrentUICulture) + words[1..];
    }

    private static IEnumerable<string> EnumerateExecutableCandidates(Process? desktopProcess)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static IEnumerable<string> EnvironmentCandidates()
        {
            yield return Environment.GetEnvironmentVariable("CODEX_PALETTE_APP_SERVER") ?? string.Empty;
            yield return Environment.GetEnvironmentVariable("CODEX_EXECUTABLE") ?? string.Empty;
        }

        foreach (var value in EnvironmentCandidates())
        {
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
            {
                yield return value;
            }
        }

        foreach (var value in EnumerateBundledExecutables(desktopProcess))
        {
            if (seen.Add(value))
            {
                yield return value;
            }
        }

        foreach (var value in new[] { "codex.exe", "codex" })
        {
            if (seen.Add(value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string> EnumerateBundledExecutables(Process? desktopProcess)
    {
        string? mainPath = null;
        try
        {
            mainPath = desktopProcess?.MainModule?.FileName;
        }
        catch
        {
            // Packaged process metadata can be restricted.
        }

        if (string.IsNullOrWhiteSpace(mainPath))
        {
            yield break;
        }

        var executableDirectory = Path.GetDirectoryName(mainPath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
        {
            yield break;
        }

        var relativeCandidates = new[]
        {
            "codex.exe",
            Path.Combine("resources", "codex.exe"),
            Path.Combine("resources", "bin", "codex.exe"),
            Path.Combine("resources", "codex", "codex.exe"),
            Path.Combine("app", "resources", "codex.exe"),
        };
        foreach (var relative in relativeCandidates)
        {
            var candidate = Path.Combine(executableDirectory, relative);
            if (File.Exists(candidate))
            {
                yield return candidate;
            }
        }

        var root = new DirectoryInfo(executableDirectory);
        while (root.Parent is not null &&
               !root.Name.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase))
        {
            root = root.Parent;
        }

        try
        {
            foreach (var candidate in Directory
                         .EnumerateFiles(root.FullName, "codex*.exe", SearchOption.AllDirectories)
                         .Take(8))
            {
                yield return candidate;
            }
        }
        catch
        {
            // The WindowsApps package can deny recursive enumeration.
        }
    }

    private static IEnumerable<string> EnumerateCacheCandidates(string? discoveredCodexHome)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var homes = new[]
        {
            discoveredCodexHome,
            Environment.GetEnvironmentVariable("CODEX_HOME"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"),
        };
        foreach (var home in homes)
        {
            if (string.IsNullOrWhiteSpace(home))
            {
                continue;
            }

            var candidate = Path.Combine(home, "models_cache.json");
            if (File.Exists(candidate) && seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        var packages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages");
        if (!Directory.Exists(packages))
        {
            yield break;
        }

        IEnumerable<string> packageDirectories;
        try
        {
            packageDirectories = Directory.EnumerateDirectories(packages, "OpenAI.Codex_*").ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var package in packageDirectories)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory
                    .EnumerateFiles(package, "models_cache.json", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(4)
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (seen.Add(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        if (!TryGetProperty(element, out var value, names) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private sealed class CodexAppServerException : Exception
    {
        public CodexAppServerException(string message, string? codexHome = null, Exception? inner = null)
            : base(message, inner)
        {
            CodexHome = codexHome;
        }

        public string? CodexHome { get; }
    }
}
