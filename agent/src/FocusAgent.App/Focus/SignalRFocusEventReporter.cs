using System.Text.Json;
using FocusAgent.Core.Focus;
using FocusAgent.Core.Realtime;

namespace FocusAgent.App.Focus;

public sealed class SignalRFocusEventReporter : IFocusEventReporter
{
    private const string ForegroundChangeKind = "ForegroundChange";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ISessionHubConnection _hub;
    private readonly TimeProvider _clock;

    public SignalRFocusEventReporter(ISessionHubConnection hub, TimeProvider clock)
    {
        _hub = hub;
        _clock = clock;
    }

    public Task ReportForegroundChangeAsync(Guid sessionId, ForegroundChange change, bool blocked, CancellationToken ct = default)
    {
        var payload = new ForegroundChangePayload(
            change.App.ProcessName,
            change.App.ExecutablePath,
            change.App.SignedPublisher,
            blocked,
            change.WindowTitle);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return _hub.ReportEventAsync(sessionId, ForegroundChangeKind, json, _clock.GetUtcNow(), ct);
    }

    private sealed record ForegroundChangePayload(
        string ProcessName,
        string? ExePath,
        string? Publisher,
        bool Blocked,
        string? WindowTitle);
}
