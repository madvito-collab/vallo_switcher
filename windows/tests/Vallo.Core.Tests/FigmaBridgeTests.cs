using System.Net.WebSockets;
using System.Text;
using Xunit;

namespace Vallo.Core.Tests;

/// Сервер для плагина — настоящим WebSocket-клиентом, как окно плагина в Фигме.
public class FigmaBridgeTests
{
    static async Task<ClientWebSocket> Connect(int port, string host = "127.0.0.1", string? origin = null)
    {
        var ws = new ClientWebSocket();
        if (origin is not null) ws.Options.SetRequestHeader("Origin", origin);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ws.ConnectAsync(new Uri($"ws://{host}:{port}"), timeout.Token);
        return ws;
    }

    static Task Send(ClientWebSocket ws, string json) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    static async Task<FigmaMessage?> Receive(ClientWebSocket ws)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[1 << 20];
        var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buffer, timeout.Token);
            ms.Write(buffer, 0, r.Count);
        } while (!r.EndOfMessage);
        return FigmaMessage.Decode(Encoding.UTF8.GetString(ms.ToArray()));
    }

    static (FigmaBridge, FigmaController) Start()
    {
        var bridge = new FigmaBridge(port: 0);
        bridge.Start();
        var controller = new FigmaController(bridge, new CommaService(Path.Combine(Path.GetTempPath(), "vallo-no-model")))
        {
            Spelling = () => (Spelling.Typos, Spelling.Yo, true),
        };
        return (bridge, controller);
    }

    [Fact]
    public async Task FreshPluginGetsPressAndLayersGetResult()
    {
        var (bridge, _) = Start();
        using var _b = bridge;
        using var ws = await Connect(bridge.Port);
        await Send(ws, """{"type":"hello","version":1,"file":"Лендинг","fresh":true}""");
        var press = Assert.IsType<FigmaMessage.Press>(await Receive(ws));
        Assert.Contains("Лендинг", bridge.ConnectedFiles);
        await Send(ws, $$"""{"type":"mode","id":{{press.Id}},"mode":"layers","skipped":2,"texts":[{"id":"1:2","text":"помоему еще - да"},{"id":"1:3","text":"Да"}]}""");
        var result = Assert.IsType<FigmaMessage.Result>(await Receive(ws));
        Assert.Equal(press.Id, result.Id);
        Assert.Equal(2, result.Skipped);
        Assert.Equal("1:2", Assert.Single(result.Edits).Id);
        Assert.Equal(1, result.Stats.Layers);
        Assert.Equal(1, result.Stats.Yo);
    }

    [Fact]
    public async Task ReconnectWithoutFreshGetsNothingAndTextModeCallsSelection()
    {
        var (bridge, controller) = Start();
        using var _b = bridge;
        var textMode = new TaskCompletionSource();
        controller.TextMode = () => textMode.TrySetResult();
        using var ws = await Connect(bridge.Port, host: "[::1]");
        await Send(ws, """{"type":"hello","version":1,"file":"Файл"}""");
        for (var i = 0; i < 50 && !bridge.ConnectedFiles.Contains("Файл"); i++) await Task.Delay(20);
        // переподключился сам — работы не ждёт; работу даёт нажатие
        Assert.True(controller.SendPress("Файл"));
        var press = Assert.IsType<FigmaMessage.Press>(await Receive(ws));
        await Send(ws, $$"""{"type":"mode","id":{{press.Id}},"mode":"text"}""");
        await textMode.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(controller.SendPress("Другой файл"));
    }

    [Fact]
    public async Task ClosedPluginIsForgotten()
    {
        var (bridge, _) = Start();
        using var _b = bridge;
        var ws = await Connect(bridge.Port);
        await Send(ws, """{"type":"hello","version":1,"file":"Ф","fresh":true}""");
        await Receive(ws);   // press
        Assert.Contains("Ф", bridge.ConnectedFiles);
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        for (var i = 0; i < 50 && bridge.ConnectedFiles.Count > 0; i++) await Task.Delay(20);
        Assert.Empty(bridge.ConnectedFiles);
    }

    [Fact]
    public async Task WebPageOriginRefused()
    {
        var (bridge, _) = Start();
        using var _b = bridge;
        await Assert.ThrowsAnyAsync<WebSocketException>(() => Connect(bridge.Port, origin: "https://example.com"));
        // окно плагина приходит с Origin «null» — пускаем
        using var ws = await Connect(bridge.Port, origin: "null");
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    [Fact]
    public void Origins()
    {
        Assert.True(FigmaBridge.OriginAllowed(null));
        Assert.True(FigmaBridge.OriginAllowed("null"));
        Assert.False(FigmaBridge.OriginAllowed("https://www.figma.com"));
    }
}
