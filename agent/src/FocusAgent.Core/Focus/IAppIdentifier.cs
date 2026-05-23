namespace FocusAgent.Core.Focus;

public interface IAppIdentifier
{
    AppInfo? Identify(nint windowHandle, int processId);
}
