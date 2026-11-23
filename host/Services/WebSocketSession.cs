using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace BayControl.Host.Services;

public sealed class WebSocketHub(AppState state, CommandRouter router)
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var id = Guid.NewGuid();
        _sockets[id] = socket;
        state.AddClient(id);

        try
        {
            await SendJsonAsync(socket, router.BuildStatusPayload());

            var buffer = new byte[8 * 1024];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var cmdResult = router.Handle(json);
                await SendJsonAsync(socket, new
                {
                    type = "result",
                    ok = cmdResult.Success,
                    message = cmdResult.Message
                });

                // Broadcast fresh status after mutations.
                await BroadcastStatusAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Client gone.
        }
        catch (WebSocketException)
        {
            // Client gone.
        }
        finally
        {
            _sockets.TryRemove(id, out _);
            state.RemoveClient(id);
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    public async Task BroadcastStatusAsync()
    {
        var payload = router.BuildStatusPayload();
        foreach (var socket in _sockets.Values.ToArray())
        {
            if (socket.State == WebSocketState.Open)
            {
                try
                {
                    await SendJsonAsync(socket, payload);
                }
                catch
                {
                    // ignore per-socket failures
                }
            }
        }
    }

    private static async Task SendJsonAsync(WebSocket socket, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
