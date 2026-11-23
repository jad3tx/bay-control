using System.Collections.Concurrent;

namespace BayControl.Host.Services;

public sealed class AppState
{
    private readonly ConcurrentDictionary<Guid, byte> _clients = new();
    private readonly ConcurrentQueue<string> _recentActions = new();
    private int _pausedRemote;

    public event Action? Changed;

    public bool RemotePaused => Interlocked.CompareExchange(ref _pausedRemote, 0, 0) == 1;
    public int ConnectedClients => _clients.Count;
    public IReadOnlyList<string> RecentActions => _recentActions.ToArray();

    public void AddClient(Guid id)
    {
        _clients[id] = 0;
        Changed?.Invoke();
    }

    public void RemoveClient(Guid id)
    {
        _clients.TryRemove(id, out _);
        Changed?.Invoke();
    }

    public void SetRemotePaused(bool paused)
    {
        Interlocked.Exchange(ref _pausedRemote, paused ? 1 : 0);
        Changed?.Invoke();
    }

    public void LogAction(string message)
    {
        _recentActions.Enqueue($"{DateTime.Now:HH:mm:ss} {message}");
        while (_recentActions.Count > 40 && _recentActions.TryDequeue(out _))
        {
        }

        Changed?.Invoke();
    }
}
