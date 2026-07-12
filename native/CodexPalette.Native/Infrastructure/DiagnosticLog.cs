using System.Text;

namespace CodexPalette.Native.Infrastructure;

public static class DiagnosticLog
{
    private const int MaximumEntries = 4000;
    private static readonly object Gate = new();
    private static readonly List<string> Entries = [];
    private static readonly string FilePath;
    private static bool _fileAvailable;

    static DiagnosticLog()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexPalette");
        FilePath = Path.Combine(directory, "diagnostics-latest.log");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                FilePath,
                "Codex Palette temporary UI Automation diagnostics" + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _fileAvailable = true;
        }
        catch
        {
            _fileAvailable = false;
        }
    }

    public static event EventHandler<string>? EntryAdded;
    public static event EventHandler? Cleared;

    public static string LogPath => FilePath;

    public static string SnapshotText
    {
        get
        {
            lock (Gate)
            {
                return string.Join(Environment.NewLine, Entries);
            }
        }
    }

    public static void Write(string message)
    {
        var normalized = Normalize(message);
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [T{Environment.CurrentManagedThreadId}] {normalized}";

        lock (Gate)
        {
            Entries.Add(line);
            if (Entries.Count > MaximumEntries)
            {
                Entries.RemoveRange(0, Entries.Count - MaximumEntries);
            }

            if (_fileAvailable)
            {
                try
                {
                    File.AppendAllText(
                        FilePath,
                        line + Environment.NewLine,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
                catch
                {
                    _fileAvailable = false;
                }
            }
        }

        EntryAdded?.Invoke(null, line);
    }

    public static void WriteException(string context, Exception exception) =>
        Write($"{context}: {exception.GetType().Name}: {exception.Message}");

    public static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
            if (_fileAvailable)
            {
                try
                {
                    File.WriteAllText(
                        FilePath,
                        "Codex Palette temporary UI Automation diagnostics" + Environment.NewLine,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
                catch
                {
                    _fileAvailable = false;
                }
            }
        }

        Cleared?.Invoke(null, EventArgs.Empty);
    }

    private static string Normalize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
