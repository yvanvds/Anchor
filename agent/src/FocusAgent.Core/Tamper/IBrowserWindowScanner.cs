namespace FocusAgent.Core.Tamper;

/// <summary>
/// A top-level browser window snapshot the agent's InPrivate witness inspects
/// (#148): the window handle (a stable per-window key for de-duplicating
/// repeated polls), its owning process name (e.g. <c>msedge</c>), and its title.
/// Deliberately the raw facts only — the InPrivate decision lives in
/// <see cref="InPrivateDetection"/> so it stays unit-testable without Win32.
/// </summary>
public sealed record BrowserWindow(nint Handle, string ProcessName, string? Title);

/// <summary>
/// Enumerates the titled, top-level browser windows currently open on the
/// desktop. The Native implementation walks <c>EnumWindows</c> and resolves each
/// window's process + title; <see cref="InPrivateWitnessMonitor"/> consumes the
/// snapshot and decides which are InPrivate. The indirection lets the monitor be
/// driven by a fake in tests (and a synthetic one under the dev e2e flag).
/// </summary>
public interface IBrowserWindowScanner
{
    /// <summary>
    /// Snapshot of the titled top-level windows. Best-effort: implementations
    /// swallow per-window probe failures rather than throw, so a single window
    /// the agent can't inspect never blinds the whole scan.
    /// </summary>
    IReadOnlyList<BrowserWindow> GetOpenBrowserWindows();
}
