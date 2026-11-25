using BayControl.Host.Services.Clips;

namespace BayControl.Tests;

public class ClipRetentionTests
{
    [Fact]
    public void EnforceRetention_respects_count()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bayclip-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new ClipStore(dir);
            for (var i = 0; i < 5; i++)
            {
                var temp = Path.Combine(dir, $"src{i}.mp4");
                File.WriteAllBytes(temp, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
                store.Register($"shot{i}", CameraRoles.FaceOn, temp, 6000, 3000, null);
            }

            store.EnforceRetention(maxCount: 3, maxTotalMb: 2048);
            Assert.Equal(3, store.List(20).Count);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CameraRoles_display_names()
    {
        Assert.Equal("Face-on", CameraRoles.DisplayName(CameraRoles.FaceOn));
        Assert.Equal("Down-the-line", CameraRoles.DisplayName(CameraRoles.DownTheLine));
        Assert.Equal("Club face", CameraRoles.DisplayName(CameraRoles.ClubFace));
    }
}
