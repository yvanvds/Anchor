namespace FocusAgent.Core.Focus;

public sealed record AppInfo(
    string ProcessName,
    string? ExecutablePath,
    string? SignedPublisher);
