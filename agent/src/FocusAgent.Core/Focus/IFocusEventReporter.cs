namespace FocusAgent.Core.Focus;

public interface IFocusEventReporter
{
    Task ReportForegroundChangeAsync(Guid sessionId, ForegroundChange change, bool blocked, CancellationToken ct = default);
}
