namespace FocusAgent.Core.Focus;

public sealed record ForegroundChange(
    AppInfo App,
    string? WindowTitle,
    int ProcessId,
    nint WindowHandle);
