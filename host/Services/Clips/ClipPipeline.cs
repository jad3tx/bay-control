using BayControl.Host.Models;

namespace BayControl.Host.Services.Clips;

public sealed class ClipPipeline
{
    private readonly CameraService _cameras;
    private readonly ClipStore _store;
    private readonly SettingsService _settings;
    private readonly AppState _state;

    public event Action<object>? ClipEvent;

    public ClipPipeline(
        CameraService cameras,
        ClipStore store,
        SettingsService settings,
        AppState state)
    {
        _cameras = cameras;
        _store = store;
        _settings = settings;
        _state = state;
    }

    public void OnShotReceived(BayShot shot)
    {
        if (!_settings.Current.ClipCaptureEnabled)
        {
            return;
        }

        var sessions = _cameras.Sessions;
        if (sessions.Count == 0)
        {
            ClipEvent?.Invoke(new
            {
                type = "clip.failed",
                shotId = shot.Id,
                error = "no_camera"
            });
            return;
        }

        foreach (var session in sessions.Values.ToArray())
        {
            _ = Task.Run(() => FinalizeForCameraAsync(shot, session));
        }
    }

    /// <summary>QA helper — synthesize a shot-timed clip from current buffers.</summary>
    public void TestCapture()
    {
        var fake = new BayShot(
            Id: Guid.NewGuid().ToString("N"),
            ReceivedAt: DateTimeOffset.UtcNow,
            Source: "manual",
            DeviceId: "test_clip",
            SourceShotNumber: null,
            Ball: new BallMetrics(0, 0, 0, 0, 0));
        OnShotReceived(fake);
    }

    private async Task FinalizeForCameraAsync(BayShot shot, CameraCaptureSession session)
    {
        var pre = Math.Max(500, _settings.Current.ClipPreRollMs);
        var post = Math.Max(500, _settings.Current.ClipPostRollMs);
        var impact = shot.ReceivedAt;
        var from = impact - TimeSpan.FromMilliseconds(pre);
        var to = impact + TimeSpan.FromMilliseconds(post);

        try
        {
            // Wait for post-roll frames to accumulate.
            var waitUntil = to + TimeSpan.FromMilliseconds(200);
            var delay = waitUntil - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            var frames = session.Ring.Snapshot(from, to);
            if (frames.Count < 5)
            {
                ClipEvent?.Invoke(new
                {
                    type = "clip.failed",
                    shotId = shot.Id,
                    cameraRole = session.Role,
                    error = "camera_busy"
                });
                _state.LogAction(
                    $"clip.fail:{session.Role}:few_frames(got={frames.Count},total={session.FramesCaptured},err={session.LastError ?? "none"})");
                return;
            }

            var width = _settings.Current.ClipWidth;
            var height = _settings.Current.ClipHeight;
            var spanSec = (frames[^1].Timestamp - frames[0].Timestamp).TotalSeconds;
            var fps = spanSec >= 0.5
                ? Math.Clamp((int)Math.Round((frames.Count - 1) / spanSec), CameraCaptureSession.MinFps, CameraCaptureSession.MaxFps)
                : Math.Clamp(_settings.Current.ClipFps, CameraCaptureSession.MinFps, CameraCaptureSession.MaxFps);
            var temp = ClipEncoder.EncodeToTempMp4(frames, fps, width, height);
            try
            {
                var durationMs = (int)(frames[^1].Timestamp - frames[0].Timestamp).TotalMilliseconds;
                if (durationMs < 500)
                {
                    durationMs = pre + post;
                }

                var entry = _store.Register(
                    shot.Id,
                    session.Role,
                    temp,
                    durationMs,
                    impactOffsetMs: pre,
                    shot);

                _store.EnforceRetention(
                    _settings.Current.ClipRetentionCount,
                    _settings.Current.ClipMaxTotalMb);

                ClipEvent?.Invoke(new
                {
                    type = "clip.ready",
                    clipId = entry.Id,
                    shotId = shot.Id,
                    cameraRole = entry.CameraRole,
                    durationMs = entry.DurationMs,
                    impactOffsetMs = entry.ImpactOffsetMs
                });
                _state.LogAction($"clip.ready:{session.Role}:{entry.Id}");
            }
            finally
            {
                try { File.Delete(temp); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            ClipEvent?.Invoke(new
            {
                type = "clip.failed",
                shotId = shot.Id,
                cameraRole = session.Role,
                error = ex.Message
            });
            _state.LogAction($"clip.fail:{session.Role}:{ex.Message}");
        }
    }
}
