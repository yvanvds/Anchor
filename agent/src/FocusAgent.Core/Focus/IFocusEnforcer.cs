namespace FocusAgent.Core.Focus;

public interface IFocusEnforcer
{
    void RememberAllowed(nint windowHandle);

    /// <summary>
    /// Minimizes the off-list window. Returns true if focus was returned to a
    /// previously remembered allowed window; false if there was nothing valid
    /// to fall back to (caller should surface the overlay).
    /// </summary>
    bool Block(nint offendingWindowHandle);

    void Reset();
}
