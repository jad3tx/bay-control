using OpenCvSharp;

namespace BayControl.Host.Services.Clips;

/// <summary>
/// Time-indexed JPEG frame ring for one camera (covers pre-roll + in-flight post-roll collect).
/// </summary>
public sealed class ClipRingBuffer : IDisposable
{
    private readonly object _gate = new();
    private readonly LinkedList<FrameItem> _frames = new();
    private readonly int _keepMs;

    public ClipRingBuffer(int keepMs)
    {
        _keepMs = Math.Max(keepMs, 1000);
    }

    public void Add(Mat bgrFrame, DateTimeOffset timestamp)
    {
        byte[] jpeg;
        try
        {
            Cv2.ImEncode(".jpg", bgrFrame, out jpeg, new ImageEncodingParam(ImwriteFlags.JpegQuality, 75));
        }
        catch
        {
            return;
        }

        lock (_gate)
        {
            _frames.AddLast(new FrameItem(timestamp, jpeg, bgrFrame.Width, bgrFrame.Height));
            TrimUnlocked(timestamp);
        }
    }

    public IReadOnlyList<FrameItem> Snapshot(DateTimeOffset from, DateTimeOffset to)
    {
        lock (_gate)
        {
            return _frames
                .Where(f => f.Timestamp >= from && f.Timestamp <= to)
                .Select(f => f with { Jpeg = (byte[])f.Jpeg.Clone() })
                .ToList();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _frames.Clear();
        }
    }

    private void TrimUnlocked(DateTimeOffset now)
    {
        var cutoff = now - TimeSpan.FromMilliseconds(_keepMs);
        while (_frames.First is not null && _frames.First.Value.Timestamp < cutoff)
        {
            _frames.RemoveFirst();
        }
    }

    public readonly record struct FrameItem(DateTimeOffset Timestamp, byte[] Jpeg, int Width, int Height);
}
