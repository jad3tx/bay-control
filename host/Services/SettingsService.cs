using System.Text.Json;

namespace BayControl.Host.Services;

public sealed class AppSettings
{
    public int Port { get; set; } = 8787;
    public bool AutostartEnabled { get; set; } = true;
    public bool FirstRunCompleted { get; set; }
    public string? Pin { get; set; }
    public string? Token { get; set; }
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
