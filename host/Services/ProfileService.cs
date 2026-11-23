using System.Text.Json;
using BayControl.Host.Models;

namespace BayControl.Host.Services;

public sealed class ProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, SimProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private string? _manualOverrideId;

    public IReadOnlyDictionary<string, SimProfile> Profiles => _profiles;
    public string? ManualOverrideId => _manualOverrideId;

    public void LoadFromDirectory(string directory)
    {
        _profiles.Clear();
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Profile directory not found: {directory}");
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (Path.GetFileName(file).Equals("schema.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var json = File.ReadAllText(file);
            var profile = JsonSerializer.Deserialize<SimProfile>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Failed to parse profile: {file}");

            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                profile.Id = Path.GetFileNameWithoutExtension(file);
            }

            _profiles[profile.Id] = profile;
        }

        if (_profiles.Count == 0)
        {
            throw new InvalidOperationException($"No sim profiles found in {directory}");
        }
    }

    public void SetManualOverride(string? profileId)
    {
        if (profileId is null || profileId.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            _manualOverrideId = null;
            return;
        }

        if (!_profiles.ContainsKey(profileId))
        {
            throw new ArgumentException($"Unknown profile '{profileId}'");
        }

        _manualOverrideId = profileId;
    }

    public SimProfile? ResolveActive(string? processName, string? windowTitle)
    {
        // Focus safety: never treat a non-sim window as active, even with manual override.
        if (!LooksLikeSim(processName, windowTitle))
        {
            return null;
        }

        if (_manualOverrideId is not null && _profiles.TryGetValue(_manualOverrideId, out var forced))
        {
            return forced;
        }

        foreach (var profile in _profiles.Values)
        {
            if (MatchesProfile(profile, processName, windowTitle))
            {
                return profile;
            }
        }

        // A known sim is focused but process/title didn't map cleanly — fall back to first profile.
        return _profiles.Values.FirstOrDefault();
    }

    public bool LooksLikeSim(string? processName, string? windowTitle) =>
        _profiles.Values.Any(p => MatchesProfile(p, processName, windowTitle));

    private static bool MatchesProfile(SimProfile profile, string? processName, string? windowTitle)
    {
        if (processName is not null &&
            profile.ProcessNames.Any(p => processName.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (windowTitle is not null &&
            profile.WindowTitleContains.Any(t => windowTitle.Contains(t, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    public bool TryGetAction(SimProfile profile, string actionId, out ActionMapping mapping)
    {
        return profile.Actions.TryGetValue(actionId, out mapping!);
    }
}
