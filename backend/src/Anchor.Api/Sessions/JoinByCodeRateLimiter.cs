using System.Collections.Concurrent;

namespace Anchor.Api.Sessions;

/// <summary>
/// Per-user sliding-window limiter for the manual join-by-code endpoint.
/// Slows brute-force of the 6-digit code space without locking out a
/// fat-fingered student. Failed attempts only — a successful join clears
/// the user's bucket. Singleton, in-memory: single-process backend, low
/// volume.
/// </summary>
public sealed class JoinByCodeRateLimiter
{
    public const int MaxFailedAttemptsPerWindow = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<Guid, List<DateTimeOffset>> _attempts = new();

    public JoinByCodeRateLimiter(TimeProvider clock)
    {
        _clock = clock;
    }

    public bool IsBlocked(Guid userId)
    {
        if (!_attempts.TryGetValue(userId, out var list))
            return false;
        lock (list)
        {
            Prune(list);
            return list.Count >= MaxFailedAttemptsPerWindow;
        }
    }

    public void RecordFailure(Guid userId)
    {
        var list = _attempts.GetOrAdd(userId, _ => new List<DateTimeOffset>());
        lock (list)
        {
            Prune(list);
            list.Add(_clock.GetUtcNow());
        }
    }

    public void Reset(Guid userId) => _attempts.TryRemove(userId, out _);

    private void Prune(List<DateTimeOffset> list)
    {
        var cutoff = _clock.GetUtcNow() - Window;
        var firstKept = list.FindIndex(t => t > cutoff);
        if (firstKept < 0)
            list.Clear();
        else if (firstKept > 0)
            list.RemoveRange(0, firstKept);
    }
}
