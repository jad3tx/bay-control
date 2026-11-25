using BayControl.Host.Models;

namespace BayControl.Host.Services.Launch;

public sealed class ShotIngestService
{
    private readonly object _gate = new();
    private readonly LinkedList<BayShot> _shots = new();
    private int _limit = 30;

    public event Action<BayShot>? ShotReceived;

    public BayShot? Latest
    {
        get
        {
            lock (_gate)
            {
                return _shots.First?.Value;
            }
        }
    }

    public string? LatestId => Latest?.Id;

    public void SetHistoryLimit(int limit)
    {
        _limit = Math.Clamp(limit, 1, 200);
        Trim();
    }

    public BayShot Ingest(BayShot shot)
    {
        lock (_gate)
        {
            _shots.AddFirst(shot);
            TrimUnlocked();
        }

        ShotReceived?.Invoke(shot);
        return shot;
    }

    public IReadOnlyList<BayShot> GetRecent(int limit = 30)
    {
        lock (_gate)
        {
            return _shots.Take(Math.Clamp(limit, 1, 200)).ToList();
        }
    }

    private void Trim()
    {
        lock (_gate)
        {
            TrimUnlocked();
        }
    }

    private void TrimUnlocked()
    {
        while (_shots.Count > _limit)
        {
            _shots.RemoveLast();
        }
    }
}
