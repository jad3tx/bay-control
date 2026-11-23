using BayControl.Host.Services;

namespace BayControl.Tests;

public class ProfileServiceTests
{
    private static string ProfilesDir
    {
        get
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "profiles"));
            if (Directory.Exists(root))
            {
                return root;
            }

            // When copied beside test output
            var copied = Path.Combine(AppContext.BaseDirectory, "profiles");
            return copied;
        }
    }

    [Fact]
    public void Loads_gspro_and_pureplay()
    {
        var svc = new ProfileService();
        svc.LoadFromDirectory(ProfilesDir);

        Assert.True(svc.Profiles.ContainsKey("gspro"));
        Assert.True(svc.Profiles.ContainsKey("pureplay"));
        Assert.True(svc.Profiles["gspro"].Actions.ContainsKey("mulligan"));
        Assert.Equal(new[] { "ctrl", "m" }, svc.Profiles["gspro"].Actions["mulligan"].Keys);
    }

    [Fact]
    public void ResolveActive_matches_process_name()
    {
        var svc = new ProfileService();
        svc.LoadFromDirectory(ProfilesDir);

        var profile = svc.ResolveActive("GSPro", "GSPro - Some Course");
        Assert.NotNull(profile);
        Assert.Equal("gspro", profile!.Id);

        var pure = svc.ResolveActive("Pureplay", "Pureplay");
        Assert.NotNull(pure);
        Assert.Equal("pureplay", pure!.Id);
    }

    [Fact]
    public void Manual_override_wins_when_sim_focused()
    {
        var svc = new ProfileService();
        svc.LoadFromDirectory(ProfilesDir);
        svc.SetManualOverride("pureplay");

        var profile = svc.ResolveActive("GSPro", "GSPro");
        Assert.Equal("pureplay", profile!.Id);

        svc.SetManualOverride("auto");
        profile = svc.ResolveActive("GSPro", "GSPro");
        Assert.Equal("gspro", profile!.Id);
    }

    [Fact]
    public void Manual_override_does_not_arm_non_sim_windows()
    {
        var svc = new ProfileService();
        svc.LoadFromDirectory(ProfilesDir);
        svc.SetManualOverride("gspro");

        Assert.Null(svc.ResolveActive("notepad", "Untitled - Notepad"));
    }

    [Fact]
    public void Shared_actions_exist_in_both_profiles()
    {
        var svc = new ProfileService();
        svc.LoadFromDirectory(ProfilesDir);

        string[] required =
        [
            "mulligan", "next_club", "prev_club", "heat_map", "flyover",
            "shot_cam", "scorecard", "aim_left", "aim_right", "reset_aim"
        ];

        foreach (var action in required)
        {
            Assert.True(svc.Profiles["gspro"].Actions.ContainsKey(action), $"gspro missing {action}");
            Assert.True(svc.Profiles["pureplay"].Actions.ContainsKey(action), $"pureplay missing {action}");
        }
    }
}
