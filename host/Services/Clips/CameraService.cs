using OpenCvSharp;

namespace BayControl.Host.Services.Clips;

public sealed class CameraService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly object _gate = new();
    private readonly Dictionary<string, CameraCaptureSession> _sessions = new();

    public CameraService(SettingsService settings)
    {
        _settings = settings;
    }

    public event Action? Changed;

    public IReadOnlyList<VideoDeviceInfo> EnumerateDevices()
    {
        HashSet<int> activeIndices;
        lock (_gate)
        {
            activeIndices = _sessions.Values
                .Select(s => int.TryParse(s.DeviceId, out var idx) ? idx : -1)
                .Where(idx => idx >= 0)
                .ToHashSet();
        }

        var list = new List<VideoDeviceInfo>();
        // Probe indices 0..7 — avoid reopening indices already held by an active session.
        for (var i = 0; i < 8; i++)
        {
            if (activeIndices.Contains(i))
            {
                list.Add(new VideoDeviceInfo(Id: i.ToString(), Name: $"Camera {i}", Index: i));
                continue;
            }

            try
            {
                using var cap = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                if (!cap.IsOpened())
                {
                    cap.Open(i, VideoCaptureAPIs.MSMF);
                }

                if (!cap.IsOpened())
                {
                    continue;
                }

                list.Add(new VideoDeviceInfo(Id: i.ToString(), Name: $"Camera {i}", Index: i));
                cap.Release();
            }
            catch
            {
                // skip
            }
        }

        return list;
    }

    public IReadOnlyDictionary<string, CameraCaptureSession> Sessions
    {
        get
        {
            lock (_gate)
            {
                return _sessions.ToDictionary(kv => kv.Key, kv => kv.Value);
            }
        }
    }

    public void ApplySettingsAndRestart()
    {
        StopAll();
        if (!_settings.Current.ClipCaptureEnabled)
        {
            Changed?.Invoke();
            return;
        }

        var keepMs = _settings.Current.ClipPreRollMs + _settings.Current.ClipPostRollMs + 1500;
        var width = _settings.Current.ClipWidth;
        var height = _settings.Current.ClipHeight;
        var fps = _settings.Current.ClipFps;

        TryStart(CameraRoles.FaceOn, _settings.Current.CameraFaceOnDeviceId, keepMs, width, height, fps);
        TryStart(CameraRoles.DownTheLine, _settings.Current.CameraDtlDeviceId, keepMs, width, height, fps);
        TryStart(CameraRoles.ClubFace, _settings.Current.CameraClubFaceDeviceId, keepMs, width, height, fps);
        Changed?.Invoke();
    }

    public void StopAll()
    {
        lock (_gate)
        {
            foreach (var s in _sessions.Values)
            {
                s.Dispose();
            }

            _sessions.Clear();
        }

        Changed?.Invoke();
    }

    public string StatusSummary()
    {
        lock (_gate)
        {
            if (!_settings.Current.ClipCaptureEnabled)
            {
                return "clips off";
            }

            if (_sessions.Count == 0)
            {
                return "no cameras selected";
            }

            var now = DateTimeOffset.UtcNow;
            var parts = _sessions.Values.Select(s =>
            {
                var name = CameraRoles.DisplayName(s.Role);
                if (s.IsRunning && s.FramesCaptured > 0)
                {
                    var ageSec = s.LastFrameUtc.HasValue ? (now - s.LastFrameUtc.Value).TotalSeconds : 99;
                    if (ageSec >= 3.0)
                    {
                        return $"{name} (stalled)";
                    }

                    var fps = s.MeasuredFps;
                    return fps >= 1 ? $"{name}✓ {fps:0}fps" : $"{name}✓";
                }

                return string.IsNullOrEmpty(s.LastError) ? $"{name}…" : $"{name}✗";
            });
            return string.Join(" · ", parts);
        }
    }

    private void TryStart(string role, string? deviceId, int keepMs, int width, int height, int fps)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || !int.TryParse(deviceId, out var index))
        {
            return;
        }

        var session = new CameraCaptureSession(role, deviceId, index, width, height, fps, keepMs);
        lock (_gate)
        {
            _sessions[role] = session;
        }

        session.Start();
    }

    public void Dispose() => StopAll();
}
