namespace FocusAgent.Core.Focus;

public interface IFocusEnforcer
{
    void RememberAllowed(nint windowHandle);
    void Block(nint offendingWindowHandle);
    void Reset();
}
