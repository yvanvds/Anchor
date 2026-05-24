using Anchor.Api.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace Anchor.Api.Tests;

public sealed class JoinByCodeRateLimiterTests
{
    [Fact]
    public void IsBlocked_returns_false_when_no_attempts_recorded()
    {
        var limiter = new JoinByCodeRateLimiter(new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        Assert.False(limiter.IsBlocked(Guid.NewGuid()));
    }

    [Fact]
    public void IsBlocked_returns_true_after_max_failures_in_window()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var limiter = new JoinByCodeRateLimiter(clock);
        var user = Guid.NewGuid();

        for (var i = 0; i < JoinByCodeRateLimiter.MaxFailedAttemptsPerWindow; i++)
            limiter.RecordFailure(user);

        Assert.True(limiter.IsBlocked(user));
    }

    [Fact]
    public void IsBlocked_clears_after_window_elapses()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var limiter = new JoinByCodeRateLimiter(clock);
        var user = Guid.NewGuid();

        for (var i = 0; i < JoinByCodeRateLimiter.MaxFailedAttemptsPerWindow; i++)
            limiter.RecordFailure(user);
        Assert.True(limiter.IsBlocked(user));

        clock.Advance(JoinByCodeRateLimiter.Window + TimeSpan.FromSeconds(1));
        Assert.False(limiter.IsBlocked(user));
    }

    [Fact]
    public void Reset_drops_recorded_failures()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var limiter = new JoinByCodeRateLimiter(clock);
        var user = Guid.NewGuid();

        for (var i = 0; i < JoinByCodeRateLimiter.MaxFailedAttemptsPerWindow; i++)
            limiter.RecordFailure(user);
        limiter.Reset(user);

        Assert.False(limiter.IsBlocked(user));
    }

    [Fact]
    public void Failures_are_tracked_per_user()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var limiter = new JoinByCodeRateLimiter(clock);
        var blockedUser = Guid.NewGuid();
        var otherUser = Guid.NewGuid();

        for (var i = 0; i < JoinByCodeRateLimiter.MaxFailedAttemptsPerWindow; i++)
            limiter.RecordFailure(blockedUser);

        Assert.True(limiter.IsBlocked(blockedUser));
        Assert.False(limiter.IsBlocked(otherUser));
    }
}
