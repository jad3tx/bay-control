using OpenCvSharp;

namespace BayControl.Host.Services.Clips;

public static class ClipEncoder
{
    // Prefer Windows Media Foundation H.264 (avc1) so browsers can play clips natively
    // without an external OpenH264 DLL; fall back to FFMPEG H.264 or MP4V if unavailable.
    private static readonly (VideoCaptureAPIs Api, int FourCc)[] EncoderCandidates =
    [
        (VideoCaptureAPIs.MSMF, FourCC.H264),
        (VideoCaptureAPIs.MSMF, FourCC.FromFourChars('a', 'v', 'c', '1')),
        (VideoCaptureAPIs.FFMPEG, FourCC.H264),
        (VideoCaptureAPIs.ANY, FourCC.MP4V),
    ];

    public static string EncodeToTempMp4(
        IReadOnlyList<ClipRingBuffer.FrameItem> frames,
        int fps,
        int width,
        int height)
    {
        if (frames.Count == 0)
        {
            throw new InvalidOperationException("No frames to encode");
        }

        fps = Math.Clamp(fps, CameraCaptureSession.MinFps, CameraCaptureSession.MaxFps);
        // H.264 requires even frame dimensions.
        width = Math.Max(2, width & ~1);
        height = Math.Max(2, height & ~1);
        var path = Path.Combine(Path.GetTempPath(), $"bayclip_{Guid.NewGuid():N}.mp4");

        using var writer = OpenWriter(path, fps, width, height);
        var written = 0;

        foreach (var item in frames)
        {
            using var mat = Cv2.ImDecode(item.Jpeg, ImreadModes.Color);
            if (mat.Empty())
            {
                continue;
            }

            if (mat.Width != width || mat.Height != height)
            {
                using var resized = new Mat();
                Cv2.Resize(mat, resized, new OpenCvSharp.Size(width, height));
                writer.Write(resized);
            }
            else
            {
                writer.Write(mat);
            }

            written++;
        }

        writer.Release();
        if (written == 0)
        {
            TryDelete(path);
            throw new InvalidOperationException("No valid frames decoded for clip");
        }

        return path;
    }

    private static VideoWriter OpenWriter(string path, int fps, int width, int height)
    {
        var size = new OpenCvSharp.Size(width, height);
        foreach (var (api, fourCc) in EncoderCandidates)
        {
            TryDelete(path);
            var writer = new VideoWriter(path, api, fourCc, fps, size);
            if (writer.IsOpened())
            {
                return writer;
            }

            writer.Dispose();
        }

        TryDelete(path);
        throw new InvalidOperationException("OpenCV VideoWriter failed to open MP4 output");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }
}
