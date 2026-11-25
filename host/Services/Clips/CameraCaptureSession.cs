using OpenCvSharp;

namespace BayControl.Host.Services.Clips;

public sealed class CameraCaptureSession : IDisposable
{
    private readonly int _index;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private readonly ClipRingBuffer _ring;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private VideoCapture? _capture;

    public string Role { get; }
    public string DeviceId { get; }
    public bool IsRunning { get; private set; }
    public string? LastError { get; private set; }
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
            _capture = new VideoCapture(_index, VideoCaptureAPIs.DSHOW);
            if (!_capture.IsOpened())
            {
                _capture.Dispose();
                _capture = new VideoCapture(_index);
            }

            if (!_capture.IsOpened())
            {
                LastError = $"Could not open camera index {_index}";
                IsRunning = false;
                return;
            }

            _capture.Set(VideoCaptureProperties.FrameWidth, _width);
            _capture.Set(VideoCaptureProperties.FrameHeight, _height);
            _capture.Set(VideoCaptureProperties.Fps, _fps);

            IsRunning = true;
            LastError = null;
            using var frame = new Mat();
            while (!ct.IsCancellationRequested)
            {
                if (!_capture.Read(frame) || frame.Empty())
                {
                    Thread.Sleep(20);
                    continue;
                }

                using var sized = new Mat();
                if (frame.Width != _width || frame.Height != _height)
                {
                    Cv2.Resize(frame, sized, new OpenCvSharp.Size(_width, _height));
                    _ring.Add(sized, DateTimeOffset.UtcNow);
                }
                else
                {
                    _ring.Add(frame, DateTimeOffset.UtcNow);
                }
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

    public void Dispose()
    {
        Stop();
        _ring.Dispose();
    }
}
