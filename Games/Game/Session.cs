using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Games.Game;

public sealed class Session
{
    private readonly World _world;
    private readonly WebSocket _socket;
    private readonly Player _player;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private long _lastChat;

    public Session(World world, WebSocket socket, Player player)
    {
        _world = world;
        _socket = socket;
        _player = player;
    }

    public async Task RunAsync()
    {
        var ct = _cts.Token;
        var welcome = _world.Welcome(_player);
        await SendAsync(JsonSerializer.SerializeToUtf8Bytes(welcome, Proto.Json), WebSocketMessageType.Text, ct);

        foreach (var line in _world.ChatHistory())
        {
            await SendAsync(JsonSerializer.SerializeToUtf8Bytes(
                new ChatMsg("chat", line.From, line.Text, line.Channel), Proto.Json), WebSocketMessageType.Text, ct);
        }

        var receive = ReceiveLoopAsync(ct);
        var send = SendLoopAsync(ct);

        await Task.WhenAny(receive, send);
        _world.RemovePlayer(_player.Id);
        _cts.Cancel();
        _socket.Abort();
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        var acc = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;

                acc.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                string text = Encoding.UTF8.GetString(acc.GetBuffer(), 0, (int)acc.Length);
                acc.SetLength(0);
                Handle(text, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    private void Handle(string text, CancellationToken ct)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(text);
            root = doc.RootElement.Clone();
        }
        catch (JsonException) { return; }

        if (!root.TryGetProperty("t", out var tProp) || tProp.ValueKind != JsonValueKind.String) return;
        var t = tProp.GetString();

        switch (t)
        {
            case "move":
            {
                float dx = root.TryGetProperty("dx", out var dxp) ? dxp.GetSingle() : 0;
                float dy = root.TryGetProperty("dy", out var dyp) ? dyp.GetSingle() : 0;
                _world.SetInput(_player.Id, Math.Clamp(dx, -1, 1), Math.Clamp(dy, -1, 1));
                break;
            }
            case "act":
            {
                string act = root.TryGetProperty("act", out var a) ? a.GetString() ?? "attack" : "attack";
                int target = root.TryGetProperty("id", out var i) && i.TryGetInt32(out int id) ? id : 0;
                _world.Act(_player.Id, act, target);
                break;
            }
            case "chat":
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (now - _lastChat < 400) return;
                _lastChat = now;
                string? msg = root.TryGetProperty("text", out var c) ? c.GetString() : null;
                _world.Chat(_player.Id, msg ?? "");
                break;
            }
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _socket.State == WebSocketState.Open)
        {
            try
            {
                var state = _world.BuildState(_player, out var events);
                if (state is not null) await SendAsync(JsonSerializer.SerializeToUtf8Bytes(state, Proto.Json), WebSocketMessageType.Text, ct);

                var you = _world.BuildYou(_player);
                await SendAsync(JsonSerializer.SerializeToUtf8Bytes(you, Proto.Json), WebSocketMessageType.Text, ct);

                foreach (var line in _world.NewChatFor(_player.Id))
                {
                    await SendAsync(JsonSerializer.SerializeToUtf8Bytes(
                        new ChatMsg("chat", line.From, line.Text, line.Channel), Proto.Json), WebSocketMessageType.Text, ct);
                }

                foreach (var e in events)
                {
                    if (e.Text.StartsWith("nothing", StringComparison.Ordinal))
                        await SendFloatAsync("Nothing here.", ct);
                    else if (e.Text.StartsWith("nogold", StringComparison.Ordinal))
                        await SendFloatAsync("Not enough gold.", ct);
                    else if (e.Text.StartsWith("hit:", StringComparison.Ordinal))
                    {
                        var parts = e.Text.Split(':');
                        if (parts.Length == 4 && int.TryParse(parts[1], out int ent) && int.TryParse(parts[2], out int amount))
                            await SendAsync(JsonSerializer.SerializeToUtf8Bytes(
                                new DamagedMsg("dmg", ent, amount, parts[3]), Proto.Json), WebSocketMessageType.Text, ct);
                    }
                }

                await Task.Delay(80, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException) { break; }
        }
    }

    private async Task SendFloatAsync(string text, CancellationToken ct)
    {
        var msg = new ChatMsg("float", "", text, "float");
        await SendAsync(JsonSerializer.SerializeToUtf8Bytes(msg, Proto.Json), WebSocketMessageType.Text, ct);
    }

    private async Task SendAsync(byte[] data, WebSocketMessageType type, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_socket.State != WebSocketState.Open) return;
            await _socket.SendAsync(data, type, true, ct);
        }
        catch (WebSocketException) { }
        finally { _sendLock.Release(); }
    }
}
