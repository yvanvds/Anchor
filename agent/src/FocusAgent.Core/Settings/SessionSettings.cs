namespace FocusAgent.Core.Settings;

public sealed record SessionSettings
{
    public const string SectionName = "Session";

    public TimeSpan DuplicateCoalesceWindow { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Interval between application-level Heartbeat pings sent to the backend
    /// while the agent is in an active session. The backend's HeartbeatMonitor
    /// declares the agent stale at <c>2 × IntervalSeconds</c> (default 20s) and
    /// emits a HeartbeatLost event.
    /// </summary>
    public int HeartbeatIntervalSeconds { get; init; } = 10;

    /// <summary>
    /// Interval at which the agent's InPrivate witness (#148) re-scans the open
    /// Edge windows while in a joined session. A few seconds is responsive enough
    /// for a teacher's live roster without polling the window list tightly; the
    /// scan only reports each InPrivate window once, so a short interval doesn't
    /// produce repeat events.
    /// </summary>
    public int InPrivateScanIntervalSeconds { get; init; } = 5;
}
