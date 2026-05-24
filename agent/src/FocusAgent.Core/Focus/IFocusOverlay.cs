using System.Collections.Generic;

namespace FocusAgent.Core.Focus;

/// <summary>
/// The topmost return-focus surface that takes focus when an off-list app
/// is minimized and there is no previously-allowed window to fall back to
/// (see issue #33). Concrete impl lives in the App layer (WinUI 3 window).
/// </summary>
public interface IFocusOverlay
{
    /// <summary>
    /// Show the overlay topmost. <paramref name="blockedAppName"/> is the
    /// app that was just minimized (purely informational on the overlay).
    /// Idempotent — calling Show while already visible just updates content.
    /// </summary>
    void Show(IReadOnlyList<AllowedAppRule> allowedRules, string? blockedAppName);

    /// <summary>
    /// Hide the overlay (kept alive for fast re-show). Safe to call when
    /// already hidden.
    /// </summary>
    void Hide();

    /// <summary>
    /// Permanently dispose the overlay. Must clear <c>HWND_TOPMOST</c>
    /// before destroying the window so the OS doesn't leave a phantom
    /// topmost handle (per #33 acceptance criteria).
    /// </summary>
    void Close();
}
