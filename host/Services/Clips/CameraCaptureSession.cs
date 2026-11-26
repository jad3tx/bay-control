using System.Diagnostics;
using System.Threading.Channels;
using OpenCvSharp;

namespace BayControl.Host.Services.Clips;

public sealed class CameraCaptureSession : IDisposable
{
    public const int MinFps = 10;
    public const int MaxFps = 150;

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
    private readonly Action<string>? _log;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private VideoCapture? _capture;
    private long _framesCaptured;
    private long _lastFrameTicks;
    private double _measuredFps;

    public string Role { get; }
    public string DeviceId { get; }
    public bool IsRunning { get; private set; }
    public string? LastError { get; private set; }
    public string? BackendName { get; private set; }
    public long FramesCaptured => Interlocked.Read(ref _framesCaptured);
    public double MeasuredFps => Volatile.Read(ref _measuredFps);
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
        int keepMs,
        Action<string>? log = null)
    {
        Role = role;
        DeviceId = deviceId;
        _index = index;
        _width = width;
        _height = height;
        _fps = Math.Clamp(fps, MinFps, MaxFps);
        _ring = new ClipRingBuffer(keepMs);
        _log = log;
    }

    public void Start()
    {
        Stop();
        Interlocked.Exchange(ref _framesCaptured, 0);
        Interlocked.Exchange(ref _lastFrameTicks, 0);
        Volatile.Write(ref _measuredFps, 0);
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

    private void ConfigureCapture(VideoCapture cap, bool useMjpg)
    {
        // Set FPS and FourCC BEFORE Width/Height so DirectShow configures the pin at high-FPS MJPG,
        // then re-apply after in case the backend resets properties on resize.
        cap.Set(VideoCaptureProperties.Fps, _fps);
        if (useMjpg)
        {
            cap.Set(VideoCaptureProperties.FourCC, FourCC.MJPG);
        }

        cap.Set(VideoCaptureProperties.FrameWidth, _width);
        cap.Set(VideoCaptureProperties.FrameHeight, _height);
        cap.Set(VideoCaptureProperties.Fps, _fps);
        if (useMjpg)
        {
            cap.Set(VideoCaptureProperties.FourCC, FourCC.MJPG);
        }
    }

    private void RunAsync(CancellationToken ct)
    {
        // Decouple USB frame reads from JPEG compression so high-FPS MJPEG capture (e.g. 150fps) doesn't stall.
        var encodeQueue = Channel.CreateBounded<(Mat Frame, DateTimeOffset Timestamp)>(
            new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            },
            dropped => dropped.Frame.Dispose());

        var encoderTask = Task.Run(() => EncodeLoop(encodeQueue.Reader, ct), CancellationToken.None);

        try
        {
            using var frame = new Mat();
            var anyOpened = false;
            (VideoCaptureAPIs Api, bool UseMjpg, string Label, double Fps)? bestFallback = null;

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
                ConfigureCapture(cap, useMjpg);

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

                    Thread.Sleep(10);
                }

                if (!gotFirstFrame)
                {
                    cap.Release();
                    cap.Dispose();
                    continue;
                }

                // When targeting > 35fps, measure ~600ms of live frames so we don't lock onto a 30fps backend
                // if another backend (e.g. MSMF+MJPG vs DSHOW+MJPG) can hit the requested high framerate.
                var probeFps = 0.0;
                if (_fps > 35)
                {
                    var probeClock = Stopwatch.StartNew();
                    var probeCount = 0;
                    while (!ct.IsCancellationRequested && probeClock.ElapsedMilliseconds < 600)
                    {
                        if (cap.Read(frame) && !frame.Empty())
                        {
                            probeCount++;
                        }
                        else
                        {
                            Thread.Sleep(1);
                        }
                    }

                    probeFps = probeClock.ElapsedMilliseconds > 0
                        ? probeCount * 1000.0 / probeClock.ElapsedMilliseconds
                        : 0;

                    if (probeFps < _fps * 0.65 && label is "DSHOW+MJPG" or "MSMF+MJPG")
                    {
                        if (bestFallback is null || probeFps > bestFallback.Value.Fps)
                        {
                            bestFallback = (api, useMjpg, label, probeFps);
                        }

                        cap.Release();
                        cap.Dispose();
                        continue;
                    }
                }

                CommitCapture(cap, label, probeFps, frame, encodeQueue.Writer, ct);
                break;
            }

            // If no high-FPS candidate met the threshold, fall back to the fastest working candidate we probed.
            if (_capture is null && bestFallback is not null && !ct.IsCancellationRequested)
            {
                var fb = bestFallback.Value;
                var cap = new VideoCapture(_index, fb.Api);
                if (cap.IsOpened())
                {
                    ConfigureCapture(cap, fb.UseMjpg);
                    var warmup = Stopwatch.StartNew();
                    var got = false;
                    while (!ct.IsCancellationRequested && warmup.ElapsedMilliseconds < 2500)
                    {
                        if (cap.Read(frame) && !frame.Empty())
                        {
                            got = true;
                            break;
                        }

                        Thread.Sleep(10);
                    }

                    if (got)
                    {
                        CommitCapture(cap, fb.Label, fb.Fps, frame, encodeQueue.Writer, ct);
                    }
                    else
                    {
                        cap.Release();
                        cap.Dispose();
                    }
                }
                else
                {
                    cap.Dispose();
                }
            }

            if (_capture is null)
            {
                LastError = anyOpened
                    ? $"Camera {_index} opened but delivered no frames"
                    : $"Could not open camera index {_index}";
                IsRunning = false;
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsRunning = false;
            encodeQueue.Writer.TryComplete();
            try { encoderTask.Wait(1000); } catch { /* ignore */ }
            while (encodeQueue.Reader.TryRead(out var leftover))
            {
                leftover.Frame.Dispose();
            }

            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
        }
    }

    private void CommitCapture(
        VideoCapture cap,
        string label,
        double initialFps,
        Mat frame,
        ChannelWriter<(Mat Frame, DateTimeOffset Timestamp)> writer,
        CancellationToken ct)
    {
        _capture = cap;
        BackendName = label;
        IsRunning = true;
        LastError = null;
        if (initialFps > 0)
        {
            Volatile.Write(ref _measuredFps, initialFps);
        }

        var reportedFps = cap.Get(VideoCaptureProperties.Fps);
        _log?.Invoke($"cam:{Role}:{label}({frame.Width}x{frame.Height}@prop={reportedFps:0},probe={initialFps:0}fps)");

        var fpsClock = Stopwatch.StartNew();
        var windowFrames = 0;
        EnqueueFrame(frame, writer, fpsClock, ref windowFrames);
        while (!ct.IsCancellationRequested)
        {
            if (!_capture.Read(frame) || frame.Empty())
            {
                Thread.Sleep(1);
                continue;
            }

            EnqueueFrame(frame, writer, fpsClock, ref windowFrames);
        }
    }

    private void EnqueueFrame(
        Mat frame,
        ChannelWriter<(Mat Frame, DateTimeOffset Timestamp)> writer,
        Stopwatch fpsClock,
        ref int windowFrames)
    {
        var now = DateTimeOffset.UtcNow;
        Mat owned;
        if (frame.Width != _width || frame.Height != _height)
        {
            owned = new Mat();
            Cv2.Resize(frame, owned, new OpenCvSharp.Size(_width, _height));
        }
        else
        {
            owned = frame.Clone();
        }

        if (!writer.TryWrite((owned, now)))
        {
            owned.Dispose();
        }

        Interlocked.Increment(ref _framesCaptured);
        Interlocked.Exchange(ref _lastFrameTicks, now.UtcTicks);

        windowFrames++;
        var elapsedMs = fpsClock.ElapsedMilliseconds;
        if (elapsedMs >= 1000)
        {
            Volatile.Write(ref _measuredFps, windowFrames * 1000.0 / elapsedMs);
            windowFrames = 0;
            fpsClock.Restart();
        }
    }

    private void EncodeLoop(ChannelReader<(Mat Frame, DateTimeOffset Timestamp)> reader, CancellationToken ct)
    {
        try
        {
            while (reader.WaitToReadAsync(ct).AsTask().GetAwaiter().GetResult())
            {
                while (reader.TryRead(out var item))
                {
                    using (item.Frame)
                    {
                        if (!ct.IsCancellationRequested)
                        {
                            _ring.Add(item.Frame, item.Timestamp);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    public void Dispose()
    {
        Stop();
        _ring.Dispose();
    }
}
