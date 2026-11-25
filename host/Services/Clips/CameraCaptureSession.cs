using System.Diagnostics;
using OpenCvSharp;

namespace BayControl.Host.Services.Clips;

public sealed class CameraCaptureSession : IDisposable
{
    private static readonly (VideoCaptureAPIs Api, bool UseMjpg, string Label)[] CaptureCandidates =
    [
        (VideoCaptureAPIs.DSHOW, true, "DSHOW+MJPG"),
        (VideoCaptureAPIs.MSMF, true, "MSMF+MJPG"),
        (VideoCaptureAPIs.DSHOW, false, "DSHOW"),
        (VideoCaptureAPIs.MSMF, false, "MSMF"),
    ];

    private readonly int _index;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly ClipRingBuffer _ring;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private VideoCapture? _capture;
    private long _framesCaptured;
    private long _lastFrameTicks;

    public string Role { get; }
    public string DeviceId { get; }
    public bool IsRunning { get; private set; }
    public string? LastError { get; private set; }
    public string? BackendName { get; private set; }
    public long FramesCaptured => Interlocked.Read(ref _framesCaptured);
    public DateTimeOffset? LastFrameUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastFrameTicks);
            return ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
        }
    }
    public ClipRingBuffer Ring => _ring;

    public CameraCaptureSession(
        string role,
        string deviceId,
        int index,
        int width,
        int height,
        int fps,
        int keepMs)
    {
        Role = role;
        DeviceId = deviceId;
        _index = index;
        _width = width;
        _height = height;
        _fps = Math.Clamp(fps, 10, 60);
        _ring = new ClipRingBuffer(keepMs);
    }

    public void Start()
    {
        Stop();
        Interlocked.Exchange(ref _framesCaptured, 0);
        Interlocked.Exchange(ref _lastFrameTicks, 0);
        BackendName = null;
        LastError = null;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _loop?.Wait(1500); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
        IsRunning = false;
    }

    private void RunAsync(CancellationToken ct)
    {
        try
        {
            using var frame = new Mat();
            var anyOpened = false;

            foreach (var (api, useMjpg, label) in CaptureCandidates)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var cap = new VideoCapture(_index, api);
                if (!cap.IsOpened())
                {
                    cap.Dispose();
                    continue;
                }

                anyOpened = true;
                if (useMjpg)
                {
                    cap.Set(VideoCaptureProperties.FourCC, FourCC.MJPG);
                }

                cap.Set(VideoCaptureProperties.FrameWidth, _width);
                cap.Set(VideoCaptureProperties.FrameHeight, _height);
                cap.Set(VideoCaptureProperties.Fps, _fps);

                // Verify the backend actually delivers frames before committing to it.
                var gotFirstFrame = false;
                var warmup = Stopwatch.StartNew();
                while (!ct.IsCancellationRequested && warmup.ElapsedMilliseconds < 2500)
                {
                    if (cap.Read(frame) && !frame.Empty())
                    {
                        gotFirstFrame = true;
                        break;
                    }

                    Thread.Sleep(25);
                }

                if (!gotFirstFrame)
                {
                    cap.Release();
                    cap.Dispose();
                    continue;
                }

                _capture = cap;
                BackendName = label;
                IsRunning = true;
                LastError = null;
                RecordFrame(frame);
                break;
            }

            if (_capture is null)
            {
                LastError = anyOpened
                    ? $"Camera {_index} opened but delivered no frames"
                    : $"Could not open camera index {_index}";
                IsRunning = false;
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                if (!_capture.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(15);
                    continue;
                }

                RecordFrame(frame);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsRunning = false;
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
        }
    }

    private void RecordFrame(Mat frame)
    {
        var now = DateTimeOffset.UtcNow;
        if (frame.Width != _width || frame.Height != _height)
        {
            using var sized = new Mat();
            Cv2.Resize(frame, sized, new OpenCvSharp.Size(_width, _height));
            _ring.Add(sized, now);
        }
        else
        {
            _ring.Add(frame, now);
        }

        Interlocked.Increment(ref _framesCaptured);
        Interlocked.Exchange(ref _lastFrameTicks, now.UtcTicks);
    }

    public void Dispose()
    {
        Stop();
        _ring.Dispose();
    }
}
