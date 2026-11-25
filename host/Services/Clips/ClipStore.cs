using System.Text.Json;
using BayControl.Host.Models;

namespace BayControl.Host.Services.Clips;

public sealed class ClipStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _root;
    private readonly object _gate = new();
    private readonly List<ClipManifestEntry> _manifest = new();

    public ClipStore(string? rootPath = null)
    {
        _root = rootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BayControl",
            "clips");
        Directory.CreateDirectory(_root);
        LoadManifest();
    }

    public string RootPath => _root;

    public IReadOnlyList<ClipManifestEntry> List(int limit = 20)
    {
        lock (_gate)
        {
            return _manifest
                .Where(x => File.Exists(Path.Combine(_root, x.FileName)))
                .Take(Math.Clamp(limit, 1, 200))
                .ToList();
        }
    }

    public ClipManifestEntry? Get(string id)
    {
        lock (_gate)
        {
            return _manifest.FirstOrDefault(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string? GetVideoPath(string id)
    {
        var entry = Get(id);
        if (entry is null)
        {
            return null;
        }

        var path = Path.Combine(_root, entry.FileName);
        return File.Exists(path) ? path : null;
    }

    public ClipManifestEntry Register(
        string shotId,
        string cameraRole,
        string sourcePath,
        int durationMs,
        int impactOffsetMs,
        BayShot? shotSnapshot)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var fileName = $"{stamp}_{id}_{cameraRole}.mp4";
        var dest = Path.Combine(_root, fileName);
        File.Copy(sourcePath, dest, overwrite: true);

        var entry = new ClipManifestEntry(
            Id: id,
            ShotId: shotId,
            CameraRole: cameraRole,
            CreatedAt: DateTimeOffset.Now,
            DurationMs: durationMs,
            SizeBytes: new FileInfo(dest).Length,
            ImpactOffsetMs: impactOffsetMs,
            FileName: fileName);

        var sidecar = Path.Combine(_root, $"{Path.GetFileNameWithoutExtension(fileName)}.json");
        File.WriteAllText(sidecar, JsonSerializer.Serialize(new
        {
            entry.Id,
            entry.ShotId,
            entry.CameraRole,
            entry.CreatedAt,
            entry.DurationMs,
            entry.ImpactOffsetMs,
            shot = shotSnapshot
        }, JsonOptions));

        lock (_gate)
        {
            _manifest.Insert(0, entry);
            SaveManifestUnlocked();
        }

        return entry;
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            var entry = _manifest.FirstOrDefault(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return false;
            }

            _manifest.Remove(entry);
            TryDelete(Path.Combine(_root, entry.FileName));
            TryDelete(Path.Combine(_root, Path.GetFileNameWithoutExtension(entry.FileName) + ".json"));
            SaveManifestUnlocked();
            return true;
        }
    }

    public void EnforceRetention(int maxCount, int maxTotalMb)
    {
        lock (_gate)
        {
            while (_manifest.Count > Math.Max(1, maxCount))
            {
                RemoveOldestUnlocked();
            }

            var budget = Math.Max(64, maxTotalMb) * 1024L * 1024L;
            while (_manifest.Sum(x => x.SizeBytes) > budget && _manifest.Count > 0)
            {
                RemoveOldestUnlocked();
            }

            SaveManifestUnlocked();
        }
    }

    private void RemoveOldestUnlocked()
    {
        var oldest = _manifest[^1];
        _manifest.RemoveAt(_manifest.Count - 1);
        TryDelete(Path.Combine(_root, oldest.FileName));
        TryDelete(Path.Combine(_root, Path.GetFileNameWithoutExtension(oldest.FileName) + ".json"));
    }

    private void LoadManifest()
    {
        var path = Path.Combine(_root, "manifest.json");
        if (!File.Exists(path))
        {
            // Rebuild from files on disk.
            foreach (var file in Directory.EnumerateFiles(_root, "*.mp4")
                         .OrderByDescending(File.GetCreationTimeUtc))
            {
                var name = Path.GetFileName(file);
                var id = Guid.NewGuid().ToString("N")[..12];
                var role = CameraRoles.FaceOn;
                var parts = Path.GetFileNameWithoutExtension(name).Split('_');
                if (parts.Length >= 3)
                {
                    id = parts[1];
                    role = parts[^1];
                }

                _manifest.Add(new ClipManifestEntry(
                    Id: id,
                    ShotId: "",
                    CameraRole: role,
                    CreatedAt: new DateTimeOffset(File.GetCreationTime(file)),
                    DurationMs: 6000,
                    SizeBytes: new FileInfo(file).Length,
                    ImpactOffsetMs: 3000,
                    FileName: name));
            }

            SaveManifestUnlocked();
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var list = JsonSerializer.Deserialize<List<ClipManifestEntry>>(json, JsonOptions);
            if (list is not null)
            {
                var existing = list.Where(x => File.Exists(Path.Combine(_root, x.FileName))).ToList();
                _manifest.AddRange(existing);
                if (existing.Count != list.Count)
                {
                    SaveManifestUnlocked();
                }
            }
        }
        catch
        {
            // ignore corrupt manifest
        }
    }

    private void SaveManifestUnlocked()
    {
        var path = Path.Combine(_root, "manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(_manifest, JsonOptions));
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
