using System.Text;
using BayControl.Host.Services.Clips;
using OpenCvSharp;

namespace BayControl.Tests;

public class ClipEncoderTests
{
    [Fact]
    public void EncodeToTempMp4_produces_browser_compatible_mp4()
    {
        var frames = new List<ClipRingBuffer.FrameItem>();
        var t0 = DateTimeOffset.UtcNow;
        using (var mat = new Mat(241, 321, MatType.CV_8UC3, new Scalar(40, 140, 60)))
        {
            Cv2.ImEncode(".jpg", mat, out var jpeg);
            for (var i = 0; i < 15; i++)
            {
                frames.Add(new ClipRingBuffer.FrameItem(t0.AddMilliseconds(i * 33), jpeg, 321, 241));
            }
        }

        var output = ClipEncoder.EncodeToTempMp4(frames, fps: 30, width: 321, height: 241);
        try
        {
            Assert.True(File.Exists(output));
            var bytes = File.ReadAllBytes(output);
            Assert.True(bytes.Length > 256);

            var ascii = Encoding.Latin1.GetString(bytes);
            Assert.Contains("ftyp", ascii);
            Assert.Contains("moov", ascii);

            if (IsMsmfH264Available())
            {
                Assert.Contains("avc1", ascii);
                Assert.DoesNotContain("mp4v", ascii);
            }
        }
        finally
        {
            try { if (File.Exists(output)) File.Delete(output); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void EncodeToTempMp4_supports_high_fps_150()
    {
        var frames = new List<ClipRingBuffer.FrameItem>();
        var t0 = DateTimeOffset.UtcNow;
        using (var mat = new Mat(240, 320, MatType.CV_8UC3, new Scalar(20, 90, 180)))
        {
            Cv2.ImEncode(".jpg", mat, out var jpeg);
            for (var i = 0; i < 30; i++)
            {
                frames.Add(new ClipRingBuffer.FrameItem(t0.AddMilliseconds(i * 7), jpeg, 320, 240));
            }
        }

        var output = ClipEncoder.EncodeToTempMp4(frames, fps: 150, width: 320, height: 240);
        try
        {
            Assert.True(File.Exists(output));
            Assert.True(new FileInfo(output).Length > 256);
        }
        finally
        {
            try { if (File.Exists(output)) File.Delete(output); } catch { /* ignore */ }
        }
    }

    private static bool IsMsmfH264Available()
    {
        var probePath = Path.Combine(Path.GetTempPath(), $"bayclip_probe_{Guid.NewGuid():N}.mp4");
        try
        {
            using var writer = new VideoWriter(
                probePath,
                VideoCaptureAPIs.MSMF,
                FourCC.H264,
                30,
                new OpenCvSharp.Size(320, 240));
            return writer.IsOpened();
        }
        finally
        {
            try { if (File.Exists(probePath)) File.Delete(probePath); } catch { /* ignore */ }
        }
    }
}
