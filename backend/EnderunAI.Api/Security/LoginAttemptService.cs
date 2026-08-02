using System.Collections.Concurrent;

namespace EnderunAI.Api.Security;

public interface ILoginAttemptService
{
    bool IsLocked(string ipAddress, out TimeSpan remaining);

    void RecordFailure(string ipAddress);

    void RecordSuccess(string ipAddress);
}

public sealed class LoginAttemptService : ILoginAttemptService
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

    private sealed class State
    {
        public int FailureCount;
        public DateTime FirstFailureAtUtc;
        public DateTime? LockedUntilUtc;
    }

    private readonly ConcurrentDictionary<string, State> _states = new();

    public bool IsLocked(string ipAddress, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        if (!_states.TryGetValue(ipAddress, out var state))
            return false;

        if (state.LockedUntilUtc is null)
            return false;

        var now = DateTime.UtcNow;
        if (state.LockedUntilUtc.Value <= now)
        {
            _states.TryRemove(ipAddress, out _);
            return false;
        }

        remaining = state.LockedUntilUtc.Value - now;
        return true;
    }

    public void RecordFailure(string ipAddress)
    {
        var now = DateTime.UtcNow;

        var state = _states.AddOrUpdate(
            ipAddress,
            _ => new State { FailureCount = 1, FirstFailureAtUtc = now },
            (_, existing) =>
            {
                if (now - existing.FirstFailureAtUtc > AttemptWindow)
                {
                    existing.FailureCount = 1;
                    existing.FirstFailureAtUtc = now;
                }
                else
                {
                    existing.FailureCount++;
                }

                return existing;
            });

        if (state.FailureCount >= MaxFailures)
        {
            state.LockedUntilUtc = now.Add(LockDuration);
        }
    }

    public void RecordSuccess(string ipAddress)
    {
        _states.TryRemove(ipAddress, out _);
    }
}
