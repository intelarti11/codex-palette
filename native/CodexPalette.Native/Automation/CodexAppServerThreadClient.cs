using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexPalette.Native.Automation;

public sealed record ThreadLinkResult(
    bool IsLinked,
    string Message,
    string? ThreadId = null,
    string? ThreadLabel = null);

internal sealed class CodexAppServerThreadClient : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly Regex CommandLineTokenRegex = new(
        "(?:[^\\s\\\"]+|\\\"(?<quoted>[^\\\"]*)\\\")",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClientWebSocket? _socket;
    private AppServerEndpoint? _endpoint;
    private string? _threadId;
    private string? _threadLabel;
    private int _nextRequestId;
    private int _linked;
    private bool _disposed;

    public bool IsLinked => Volatile.Read(ref _linked) == 1;
    public string? LinkedThreadLabel => _threadLabel;

    public async Task<ThreadLinkResult> LinkAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            CloseConnection();

            var endpoints = GetEndpointCandidates();
            if (endpoints.Count == 0)
            {
                return new ThreadLinkResult(
                    false,
                    "Codex n’expose pas de canal app-server local partagé. Le fil ne peut pas être lié sans ouvrir le sélecteur.");
            }

            var failures = new List<string>();
            foreach (var endpoint in endpoints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ClientWebSocket? socket = null;
                try
                {
                    socket = await ConnectAndInitializeAsync(endpoint, cancellationToken).ConfigureAwait(false);
                    var threads = await ReadLoadedTopLevelThreadsAsync(socket, cancellationToken).ConfigureAwait(false);
                    if (threads.Count == 0)
                    {
                        failures.Add($"{endpoint.Uri}: aucun fil principal chargé");
                        socket.Dispose();
                        continue;
                    }

                    if (threads.Count != 1)
                    {
                        failures.Add($"{endpoint.Uri}: {threads.Count} fils principaux chargés");
                        socket.Dispose();
                        continue;
                    }

                    var thread = threads[0];
                    _socket = socket;
                    _endpoint = endpoint;
                    _threadId = thread.Id;
                    _threadLabel = thread.Label;
                    Volatile.Write(ref _linked, 1);
                    return new ThreadLinkResult(
                        true,
                        $"Fil lié : {thread.Label}",
                        thread.Id,
                        thread.Label);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    socket?.Dispose();
                    throw;
                }
                catch (Exception exception)
                {
                    socket?.Dispose();
                    failures.Add($"{endpoint.Uri}: {exception.Message}");
                }
            }

            var detail = failures.Count == 0
                ? string.Empty
                : " " + string.Join(" | ", failures.Take(3));
            return new ThreadLinkResult(
                false,
                "Aucun fil actif n’a pu être identifié de manière unique sur l’instance Codex." + detail);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateSettingsAsync(
        string? model,
        string? effort,
        bool updateServiceTier,
        string? serviceTier,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!IsLinked || _socket is null || _threadId is null)
            {
                throw new AutomationUnavailableException(
                    "Liez d’abord le fil actif avec le bouton chaîne. Aucun menu natif ne sera ouvert automatiquement.");
            }

            try
            {
                var loadedIds = await ReadLoadedThreadIdsAsync(_socket, cancellationToken).ConfigureAwait(false);
                if (!loadedIds.Contains(_threadId, StringComparer.Ordinal))
                {
                    throw new AutomationUnavailableException(
                        "Le fil lié n’est plus chargé dans Codex. Reliez le fil actif avant de continuer.");
                }

                var parameters = new Dictionary<string, object?>
                {
                    ["threadId"] = _threadId,
                };
                if (!string.IsNullOrWhiteSpace(model))
                {
                    parameters["model"] = model;
                }
                if (!string.IsNullOrWhiteSpace(effort))
                {
                    parameters["effort"] = effort;
                }
                if (updateServiceTier)
                {
                    parameters["serviceTier"] = serviceTier;
                }

                await SendRequestAsync(
                        _socket,
                        "thread/settings/update",
                        parameters,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                CloseConnection();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Unlink()
    {
        _gate.Wait();
        try
        {
            CloseConnection();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unlink();
        _gate.Dispose();
    }

    internal static IReadOnlyList<Uri> ParseLoopbackEndpoints(string commandLine, int processId)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<Uri>();
        }

        var tokens = Tokenize(commandLine);
        if (!tokens.Any(static token =>
                string.Equals(token, "app-server", StringComparison.OrdinalIgnoreCase)))
        {
            return Array.Empty<Uri>();
        }

        var listenValue = ReadArgument(tokens, "--listen");
        if (string.IsNullOrWhiteSpace(listenValue) ||
            !listenValue.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(listenValue, UriKind.Absolute, out var parsed) ||
            !IsLoopback(parsed))
        {
            return Array.Empty<Uri>();
        }

        if (parsed.Port > 0)
        {
            return new[] { parsed };
        }

        return GetLoopbackListenerPorts(processId)
            .Select(port => new UriBuilder(parsed) { Port = port }.Uri)
            .Distinct()
            .ToArray();
    }

    private async Task<ClientWebSocket> ConnectAndInitializeAsync(
        AppServerEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        if (!string.IsNullOrWhiteSpace(endpoint.BearerToken))
        {
            socket.Options.SetRequestHeader("Authorization", "Bearer " + endpoint.BearerToken);
        }

        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(ConnectTimeout);
                await socket.ConnectAsync(endpoint.Uri, timeout.Token).ConfigureAwait(false);
            }

            await SendRequestAsync(
                    socket,
                    "initialize",
                    new
                    {
                        clientInfo = new
                        {
                            name = "codex_palette",
                            title = "Codex Palette",
                            version = "0.7.0",
                        },
                        capabilities = new
                        {
                            experimentalApi = true,
                        },
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            await SendNotificationAsync(socket, "initialized", new { }, cancellationToken)
                .ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task<IReadOnlyList<LoadedThread>> ReadLoadedTopLevelThreadsAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var result = new List<LoadedThread>();
        foreach (var id in await ReadLoadedThreadIdsAsync(socket, cancellationToken).ConfigureAwait(false))
        {
            var response = await SendRequestAsync(
                    socket,
                    "thread/read",
                    new { threadId = id, includeTurns = false },
                    cancellationToken)
                .ConfigureAwait(false);
            if (!response.TryGetProperty("thread", out var thread) ||
                thread.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (thread.TryGetProperty("parentThreadId", out var parent) &&
                parent.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(parent.GetString()))
            {
                continue;
            }

            result.Add(new LoadedThread(id, GetThreadLabel(thread, id)));
        }

        return result
            .DistinctBy(static thread => thread.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> ReadLoadedThreadIdsAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        string? cursor = null;
        do
        {
            var response = await SendRequestAsync(
                    socket,
                    "thread/loaded/list",
                    new { cursor, limit = 100 },
                    cancellationToken)
                .ConfigureAwait(false);
            if (response.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        ids.Add(item.GetString()!);
                    }
                }
            }

            cursor = response.TryGetProperty("nextCursor", out var next) &&
                     next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return ids.Distinct(StringComparer.Ordinal).ToArray();
    }

    private async Task<JsonElement> SendRequestAsync(
        ClientWebSocket socket,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextRequestId);
        await SendJsonAsync(
                socket,
                new { method, id, @params = parameters },
                cancellationToken)
            .ConfigureAwait(false);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        while (true)
        {
            var message = await ReceiveJsonAsync(socket, timeout.Token).ConfigureAwait(false);
            if (!message.TryGetProperty("id", out var responseId) ||
                responseId.ValueKind != JsonValueKind.Number ||
                !responseId.TryGetInt32(out var value) ||
                value != id)
            {
                continue;
            }

            if (message.TryGetProperty("error", out var error))
            {
                var text = error.TryGetProperty("message", out var errorMessage) &&
                           errorMessage.ValueKind == JsonValueKind.String
                    ? errorMessage.GetString()
                    : error.GetRawText();
                throw new AutomationUnavailableException(text ?? "Codex app-server rejected the request.");
            }

            if (!message.TryGetProperty("result", out var result))
            {
                throw new AutomationUnavailableException("Codex app-server returned an invalid response.");
            }

            return result.Clone();
        }
    }

    private static Task SendNotificationAsync(
        ClientWebSocket socket,
        string method,
        object parameters,
        CancellationToken cancellationToken) =>
        SendJsonAsync(socket, new { method, @params = parameters }, cancellationToken);

    private static async Task SendJsonAsync(
        ClientWebSocket socket,
        object value,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<JsonElement> ReceiveJsonAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new AutomationUnavailableException("Codex app-server closed the shared connection.");
                }
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                stream.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            using var document = JsonDocument.Parse(stream.ToArray());
            return document.RootElement.Clone();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static IReadOnlyList<AppServerEndpoint> GetEndpointCandidates()
    {
        var result = new List<AppServerEndpoint>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(Uri uri, string? token, int? processId)
        {
            if (!IsLoopback(uri) || uri.Port <= 0 || !seen.Add(uri.AbsoluteUri))
            {
                return;
            }
            result.Add(new AppServerEndpoint(uri, token, processId));
        }

        var configured = Environment.GetEnvironmentVariable("CODEX_PALETTE_ACTIVE_APP_SERVER");
        if (Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri))
        {
            Add(
                configuredUri,
                Environment.GetEnvironmentVariable("CODEX_PALETTE_APP_SERVER_TOKEN"),
                processId: null);
        }

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (!process.ProcessName.Contains("codex", StringComparison.OrdinalIgnoreCase) &&
                        !process.ProcessName.Contains("chatgpt", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var commandLine = TryGetProcessCommandLine(process);
                    if (string.IsNullOrWhiteSpace(commandLine))
                    {
                        continue;
                    }

                    var tokens = Tokenize(commandLine);
                    var token = ReadCapabilityToken(tokens);
                    foreach (var endpoint in ParseLoopbackEndpoints(commandLine, process.Id))
                    {
                        Add(endpoint, token, process.Id);
                    }
                }
                catch
                {
                    // Packaged app process metadata can be restricted.
                }
            }
        }

        return result;
    }

    private static string? ReadCapabilityToken(IReadOnlyList<string> tokens)
    {
        var authMode = ReadArgument(tokens, "--ws-auth");
        if (string.IsNullOrWhiteSpace(authMode))
        {
            return null;
        }
        if (!string.Equals(authMode, "capability-token", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tokenFile = ReadArgument(tokens, "--ws-token-file");
        if (string.IsNullOrWhiteSpace(tokenFile) || !File.Exists(tokenFile))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(tokenFile).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> Tokenize(string commandLine) =>
        CommandLineTokenRegex.Matches(commandLine)
            .Select(match => match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value
                : match.Value)
            .ToArray();

    private static string? ReadArgument(IReadOnlyList<string> tokens, string name)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (string.Equals(token, name, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < tokens.Count ? tokens[index + 1] : null;
            }

            var prefix = name + "=";
            if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return token[prefix.Length..];
            }
        }

        return null;
    }

    private static bool IsLoopback(Uri uri)
    {
        if (!string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    private static string GetThreadLabel(JsonElement thread, string id)
    {
        foreach (var property in new[] { "name", "preview" })
        {
            if (thread.TryGetProperty(property, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return Shorten(TextNormalizer.Normalize(value.GetString()!), 54);
            }
        }

        if (thread.TryGetProperty("cwd", out var cwd) &&
            cwd.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(cwd.GetString()))
        {
            try
            {
                var directoryName = new DirectoryInfo(cwd.GetString()!).Name;
                if (!string.IsNullOrWhiteSpace(directoryName))
                {
                    return Shorten(directoryName, 54);
                }
            }
            catch
            {
                // Fall through to the stable id prefix.
            }
        }

        return "Fil " + id[..Math.Min(8, id.Length)];
    }

    private static string Shorten(string value, int maximum) =>
        value.Length <= maximum ? value : value[..(maximum - 1)] + "…";

    private static string? TryGetProcessCommandLine(Process process)
    {
        const int processCommandLineInformation = 60;
        _ = NtQueryInformationProcess(
            process.Handle,
            processCommandLineInformation,
            nint.Zero,
            0,
            out var requiredLength);
        if (requiredLength <= 0 || requiredLength > 1024 * 1024)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(requiredLength);
        try
        {
            var status = NtQueryInformationProcess(
                process.Handle,
                processCommandLineInformation,
                buffer,
                requiredLength,
                out _);
            if (status < 0)
            {
                return null;
            }

            var value = Marshal.PtrToStructure<UnicodeString>(buffer);
            return value.Buffer == nint.Zero || value.Length == 0
                ? null
                : Marshal.PtrToStringUni(value.Buffer, value.Length / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<int> GetLoopbackListenerPorts(int processId)
    {
        const int afInet = 2;
        const uint errorInsufficientBuffer = 122;
        var size = 0;
        var status = GetExtendedTcpTable(
            nint.Zero,
            ref size,
            order: false,
            afInet,
            TcpTableClass.OwnerPidListener,
            0);
        if (status != errorInsufficientBuffer || size <= 0)
        {
            return Array.Empty<int>();
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            status = GetExtendedTcpTable(
                buffer,
                ref size,
                order: false,
                afInet,
                TcpTableClass.OwnerPidListener,
                0);
            if (status != 0)
            {
                return Array.Empty<int>();
            }

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var rows = new List<int>();
            for (var index = 0; index < count; index++)
            {
                var pointer = nint.Add(buffer, sizeof(int) + index * rowSize);
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(pointer);
                if (row.OwningPid != processId)
                {
                    continue;
                }

                var address = new IPAddress(row.LocalAddress);
                if (!IPAddress.IsLoopback(address))
                {
                    continue;
                }

                var port = (ushort)IPAddress.NetworkToHostOrder((short)row.LocalPort);
                if (port > 0)
                {
                    rows.Add(port);
                }
            }

            return rows.Distinct().ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void CloseConnection()
    {
        Volatile.Write(ref _linked, 0);
        _threadId = null;
        _threadLabel = null;
        _endpoint = null;
        try
        {
            _socket?.Abort();
            _socket?.Dispose();
        }
        catch
        {
            // A disconnected app-server may already have released the socket.
        }
        _socket = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        nint processHandle,
        int processInformationClass,
        nint processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        nint tcpTable,
        ref int size,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily,
        TcpTableClass tableClass,
        uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct UnicodeString
    {
        public readonly ushort Length;
        public readonly ushort MaximumLength;
        public readonly nint Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MibTcpRowOwnerPid
    {
        public readonly uint State;
        public readonly uint LocalAddress;
        public readonly uint LocalPort;
        public readonly uint RemoteAddress;
        public readonly uint RemotePort;
        public readonly int OwningPid;
    }

    private enum TcpTableClass
    {
        OwnerPidListener = 3,
    }

    private sealed record AppServerEndpoint(Uri Uri, string? BearerToken, int? ProcessId);
    private sealed record LoadedThread(string Id, string Label);
}
