using OpenCvSharp;

namespace BayControl.Host.Services.Clips;

public static class ClipEncoder
{
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

        fps = Math.Clamp(fps, 10, 60);
        var path = Path.Combine(Path.GetTempPath(), $"bayclip_{Guid.NewGuid():N}.mp4");

        // mp4v is widely available with OpenCV Windows builds.
        using var writer = new VideoWriter(
            path,
            FourCC.MP4V,
            fps,
            new OpenCvSharp.Size(width, height));

        if (!writer.IsOpened())
        {
            throw new InvalidOperationException("OpenCV VideoWriter failed to open MP4 output");
        }

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
        }

        writer.Release();
        return path;
    }
}
