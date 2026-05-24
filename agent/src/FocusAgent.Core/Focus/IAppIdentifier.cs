namespace FocusAgent.Core.Focus;

public interface IAppIdentifier
{
    AppInfo? Identify(nint windowHandle, int processId);

    /// <summary>
    /// Brings a window matching <paramref name="rule"/> to the foreground if one
    /// is already running; otherwise tries to launch the app from the rule's
    /// value (path or process name). Returns true if anything was activated or
    /// launched. Publisher-only rules are not actionable and return false.
    /// </summary>
    bool LaunchOrActivate(AllowedAppRule rule);
}
