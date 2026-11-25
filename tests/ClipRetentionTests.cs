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

    [Fact]
    public void List_skips_entries_with_missing_video_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bayclip-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new ClipStore(dir);
            var temp = Path.Combine(dir, "src.mp4");
            File.WriteAllBytes(temp, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
            var entry = store.Register("shot1", CameraRoles.FaceOn, temp, 6000, 3000, null);
            Assert.Single(store.List(20));

            File.Delete(Path.Combine(dir, entry.FileName));
            Assert.Empty(store.List(20));

            var reloaded = new ClipStore(dir);
            Assert.Empty(reloaded.List(20));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
