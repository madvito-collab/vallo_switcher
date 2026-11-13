using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace Vallo.Core;

/// Связь с плагином Vallo в Фигме: локальный WebSocket-сервер только на 127.0.0.1 и ::1.
/// Плагин в каждом открытом файле подключается сам и говорит «hello» с именем файла.
/// Протокол — figma-plugin/PROTOCOL.md. Перенос `Sources/ValloSwitcher/FigmaBridge.swift`.
public sealed class FigmaBridge : IDisposable
{
    public const int DefaultPort = 48731;
    /// Больше этого сообщение не принимаем (тексты всех слоёв выделения).
    const int MaxMessageBytes = 64 * 1024 * 1024;

    readonly int port;
    readonly List<TcpListener> listeners = [];
    readonly CancellationTokenSource stop = new();
    readonly object gate = new();
    /// Подключения по имени файла (последнее подключение файла побеждает).
    readonly Dictionary<string, Connection> byFile = new();

    /// Сообщение от плагина (из фона) и имя его файла.
    public Action<FigmaMessage, string>? OnMessage { get; set; }
    /// Плагин впервые вышел на связь (из фона).
    public Action? OnHello { get; set; }

    public FigmaBridge(int port = DefaultPort) => this.port = port;

    /// Порт, на котором слушаем (0 в конструкторе — свободный, для тестов).
    public int Port => listeners.Count > 0 ? ((IPEndPoint)listeners[0].LocalEndpoint).Port : port;

    public IReadOnlyCollection<string> ConnectedFiles
    {
        get { lock (gate) return byFile.Keys.ToList(); }
    }

    sealed class Connection(WebSocket socket)
    {
        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim Sending { get; } = new(1, 1);
    }

    /// «localhost» в окне плагина может оказаться и 127.0.0.1, и ::1 — слушаем оба, наружу — ничего.
    public void Start()
    {
        var chosen = port;
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            try
            {
                var listener = new TcpListener(address, chosen);
                listener.Start();
                // порт 0 — система выбрала свободный: второй адрес — на том же
                chosen = ((IPEndPoint)listener.LocalEndpoint).Port;
                listeners.Add(listener);
                _ = AcceptLoop(listener);
            }
            catch (SocketException e)
            {
                // порт занят или нет IPv6 — без этого адреса, но без падения
                DebugLog.Shared.Write(() => $"figma: не слушаю {address}:{chosen} ({e.SocketErrorCode})");
            }
        }
    }

    /// Отправить сообщение плагину файла. false — плагин этого файла не на связи.
    public bool Send(FigmaMessage message, string file)
    {
        Connection? connection;
        lock (gate) byFile.TryGetValue(file, out connection);
        if (connection is null) return false;
        _ = SendAsync(connection, message.Encode());
        return true;
    }

    async Task SendAsync(Connection connection, string text)
    {
        await connection.Sending.WaitAsync().ConfigureAwait(false);
        try
        {
            await connection.Socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, stop.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            Forget(connection);
        }
        finally
        {
            connection.Sending.Release();
        }
    }

    async Task AcceptLoop(TcpListener listener)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false); }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = Serve(client);
        }
    }

    async Task Serve(TcpClient client)
    {
        using var _ = client;
        WebSocket? socket = null;
        Connection? connection = null;
        try
        {
            var stream = client.GetStream();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                if (!await Handshake(stream, timeout.Token).ConfigureAwait(false)) return;
            }
            socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
            {
                IsServer = true,
                KeepAliveInterval = TimeSpan.FromSeconds(30),
            });
            connection = new Connection(socket);
            string? file = null;
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            while (!stop.IsCancellationRequested)
            {
                var r = await socket.ReceiveAsync(buffer, stop.Token).ConfigureAwait(false);
                // плагин закрылся («Готово» → closePlugin) — соединение забываем сразу,
                // иначе следующее нажатие ушло бы в мёртвый сокет
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    Forget(connection);
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, stop.Token).ConfigureAwait(false);
                    break;
                }
                message.Write(buffer, 0, r.Count);
                if (message.Length > MaxMessageBytes) break;
                if (!r.EndOfMessage) continue;
                var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                if (r.MessageType != WebSocketMessageType.Text || FigmaMessage.Decode(text) is not { } m) continue;
                if (m is FigmaMessage.Hello hello)
                {
                    file = hello.File;
                    Connection? old;
                    int count;
                    lock (gate)
                    {
                        byFile.TryGetValue(file, out old);
                        byFile[file] = connection;
                        count = byFile.Count;
                    }
                    if (old is not null && old != connection) old.Socket.Abort();
                    OnHello?.Invoke();
                    DebugLog.Shared.Write(() => $"figma: плагин на связи (файлов: {count})");
                }
                if (file is not null) OnMessage?.Invoke(m, file);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            if (connection is not null) Forget(connection);
            socket?.Dispose();
        }
    }

    void Forget(Connection connection)
    {
        lock (gate)
            foreach (var (file, c) in byFile.ToList())
                if (c == connection) byFile.Remove(file);
    }

    /// Рукопожатие WebSocket. Окно плагина приходит без Origin или с Origin «null»;
    /// обычная веб-страница — со своим адресом: такую не пускаем.
    static async Task<bool> Handshake(NetworkStream stream, CancellationToken token)
    {
        var request = await ReadHead(stream, token).ConfigureAwait(false);
        if (request is null) return false;
        var lines = request.Split("\r\n");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        var origin = headers.GetValueOrDefault("Origin");
        if (!lines[0].StartsWith("GET ", StringComparison.Ordinal)
            || !headers.TryGetValue("Upgrade", out var upgrade) || !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase)
            || !headers.TryGetValue("Sec-WebSocket-Key", out var key) || key.Length == 0
            || !OriginAllowed(origin))
        {
            if (origin is not null && !OriginAllowed(origin)) DebugLog.Shared.Write("figma: чужой Origin — не пускаем");
            var refuse = Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(refuse, token).ConfigureAwait(false);
            return false;
        }
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\n\r\n");
        await stream.WriteAsync(response, token).ConfigureAwait(false);
        return true;
    }

    public static bool OriginAllowed(string? origin) => origin is null || origin == "null";

    /// Заголовок запроса до пустой строки. Клиент до ответа сервера ничего больше не шлёт — лишнего не прочтём.
    static async Task<string?> ReadHead(NetworkStream stream, CancellationToken token)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (head.Count < 16 * 1024)
        {
            if (await stream.ReadAsync(one, token).ConfigureAwait(false) == 0) return null;
            head.Add(one[0]);
            var n = head.Count;
            if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n')
                return Encoding.ASCII.GetString(head.ToArray(), 0, n - 4);
        }
        return null;
    }

    public void Dispose()
    {
        stop.Cancel();
        foreach (var l in listeners) l.Stop();
        lock (gate)
        {
            foreach (var c in byFile.Values) c.Socket.Abort();
            byFile.Clear();
        }
    }
}
