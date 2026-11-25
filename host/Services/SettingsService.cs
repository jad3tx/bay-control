using System.Text.Json;

namespace BayControl.Host.Services;

public sealed class AppSettings
{
    public int Port { get; set; } = 8787;
    public bool AutostartEnabled { get; set; } = true;
    public bool FirstRunCompleted { get; set; }
    public string? Pin { get; set; }
    public string? Token { get; set; }

    // Launch monitor / shot tools (Phase 1)
    public bool NovaEnabled { get; set; } = true;
    public string? NovaHost { get; set; }
    public int NovaWsPort { get; set; } = 2920;
    public bool OpenConnectTeeEnabled { get; set; }
    public int OpenConnectListenPort { get; set; } = 921;
    public string OpenConnectForwardHost { get; set; } = "127.0.0.1";
    public int OpenConnectForwardPort { get; set; } = 9210;
    public string DisplaySpeedUnit { get; set; } = "mph";
    public string DisplayDistanceUnit { get; set; } = "yards";
    public int ShotHistoryLimit { get; set; } = 30;

    // Swing clips (Phase 2) — dual USB cams; club-face optional later
    public bool ClipCaptureEnabled { get; set; } = true;
    public string? CameraFaceOnDeviceId { get; set; }
    public string? CameraDtlDeviceId { get; set; }
    public string? CameraClubFaceDeviceId { get; set; }
    public int ClipPreRollMs { get; set; } = 3000;
    public int ClipPostRollMs { get; set; } = 3000;
    public int ClipRetentionCount { get; set; } = 50;
    public int ClipMaxTotalMb { get; set; } = 2048;
    public int ClipWidth { get; set; } = 1280;
    public int ClipHeight { get; set; } = 720;
    public int ClipFps { get; set; } = 30;
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BayControl");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Load();
    }

    public string SettingsPath => _path;

    public void Load()
    {
        if (!File.Exists(_path))
        {
            Current = new AppSettings();
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
    }
}
