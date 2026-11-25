using BayControl.Host.Models;

namespace BayControl.Host.Services.Launch;

/// <summary>
/// Owns Nova (and later Tee) lifecycle and publishes launch health for WS/REST.
/// </summary>
public sealed class LaunchMonitorCoordinator
{
    private readonly NovaWebSocketClient _nova;
    private readonly SettingsService _settings;
    private readonly ShotIngestService _ingest;

    public LaunchMonitorCoordinator(
        NovaWebSocketClient nova,
        SettingsService settings,
        ShotIngestService ingest)
    {
        _nova = nova;
        _settings = settings;
        _ingest = ingest;
        _ingest.SetHistoryLimit(_settings.Current.ShotHistoryLimit);
    }

    public event Action? StatusChanged
    {
        add => _nova.StatusChanged += value;
        remove => _nova.StatusChanged -= value;
    }

    public void Start() => _nova.Start();

    public void Stop() => _nova.Stop();

    public object BuildLaunchPayload() => new
    {
        nova = ToDto(_nova.Status),
        tee = new
        {
            state = _settings.Current.OpenConnectTeeEnabled ? "not_implemented" : "disabled",
            detail = _settings.Current.OpenConnectTeeEnabled
                ? "Open Connect Tee comes in a follow-up build"
                : "Tee off (Nova-only bay)"
        }
    };

    public string NovaState => _nova.Status.State;

    public string? NovaDetail => _nova.Status.Detail;

    public string TeeState => _settings.Current.OpenConnectTeeEnabled ? "not_implemented" : "disabled";

    private static object ToDto(AdapterStatus s) => new
    {
        state = s.State,
        detail = s.Detail,
        firmware = s.Firmware,
        shotCount = s.ShotCount,
        lastMessageAt = s.LastMessageAt
    };
}
