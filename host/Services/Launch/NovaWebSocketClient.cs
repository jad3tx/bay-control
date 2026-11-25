using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BayControl.Host.Models;
using Zeroconf;

namespace BayControl.Host.Services.Launch;

public sealed class NovaWebSocketClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ShotIngestService _ingest;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    private AdapterStatus _status = new("disabled", "Nova not started");

    public AdapterStatus Status => _status;

    public event Action? StatusChanged;

    public NovaWebSocketClient(ShotIngestService ingest, SettingsService settings)
    {
        _ingest = ingest;
        _settings = settings;
    }

    public void Start()
    {
        Stop();
        if (!_settings.Current.NovaEnabled)
        {
            SetStatus(new AdapterStatus("disabled", "Nova disabled in settings"));
            return;
        }

        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token));
    }

    public void Stop()
    {
        try { _loopCts?.Cancel(); } catch { /* ignore */ }
        _loopCts?.Dispose();
        _loopCts = null;
        _loopTask = null;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var delayMs = 1000;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var endpoint = await ResolveEndpointAsync(ct);
                if (endpoint is null)
                {
                    SetStatus(new AdapterStatus("searching", "Nova not found — check Wi‑Fi / power, or set Nova host in settings"));
                    await Task.Delay(delayMs, ct);
                    delayMs = Math.Min(delayMs * 2, 15_000);
                    continue;
                }

                SetStatus(new AdapterStatus("connecting", $"Connecting to {endpoint}"));
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(endpoint), ct);
                SetStatus(new AdapterStatus("connected", endpoint));
                delayMs = 1000;

                var buffer = new byte[16 * 1024];
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }

                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    HandleMessage(json);
                }

                SetStatus(new AdapterStatus("disconnected", "Nova socket closed — reconnecting"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetStatus(new AdapterStatus("error", ex.Message));
                try { await Task.Delay(delayMs, ct); } catch { break; }
                delayMs = Math.Min(delayMs * 2, 15_000);
            }
        }

        SetStatus(new AdapterStatus("stopped", "Nova client stopped"));
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (string.Equals(type, "shot", StringComparison.OrdinalIgnoreCase))
            {
                var shot = ShotNormalizer.FromNovaJson(root);
                _ingest.Ingest(shot);
                SetStatus(new AdapterStatus(
                    "connected",
                    _status.Detail,
                    _status.Firmware,
                    shot.SourceShotNumber,
                    DateTimeOffset.UtcNow));
            }
            else if (string.Equals(type, "status", StringComparison.OrdinalIgnoreCase))
            {
                var fw = root.TryGetProperty("firmware_version", out var f) ? f.GetString() : _status.Firmware;
                int? count = null;
                if (root.TryGetProperty("shot_count", out var sc) && sc.TryGetInt32(out var n))
                {
                    count = n;
                }

                SetStatus(new AdapterStatus("connected", _status.Detail, fw, count, DateTimeOffset.UtcNow));
            }
        }
        catch
        {
            // Ignore malformed frames.
        }
    }

    private async Task<string?> ResolveEndpointAsync(CancellationToken ct)
    {
        var host = _settings.Current.NovaHost;
        var port = _settings.Current.NovaWsPort > 0 ? _settings.Current.NovaWsPort : 2920;

        if (!string.IsNullOrWhiteSpace(host))
        {
            return $"ws://{host.Trim()}:{port}/";
        }

        try
        {
            var results = await ZeroconfResolver.ResolveAsync(
                "_openlaunch-ws._tcp.local.",
                TimeSpan.FromSeconds(3),
                retries: 1,
                retryDelayMilliseconds: 200);

            foreach (var device in results)
            {
                foreach (var svc in device.Services.Values)
                {
                    var p = svc.Port > 0 ? svc.Port : port;
                    var ip = device.IPAddresses.FirstOrDefault() ?? device.DisplayName;
                    if (!string.IsNullOrWhiteSpace(ip))
                    {
                        return $"ws://{ip}:{p}/";
                    }
                }
            }
        }
        catch
        {
            // Discovery failed — fall through.
        }

        _ = ct;
        return null;
    }

    private void SetStatus(AdapterStatus status)
    {
        _status = status;
        StatusChanged?.Invoke();
    }
}
