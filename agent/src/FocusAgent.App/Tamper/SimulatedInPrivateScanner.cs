using FocusAgent.Core.Tamper;

namespace FocusAgent.App.Tamper;

/// <summary>
/// Dev-only <see cref="IBrowserWindowScanner"/> that always reports one synthetic
/// Edge InPrivate window, registered under the <c>--simulate-inprivate</c> flag
/// (#148). A real InPrivate Edge window can't be driven reliably in a headless
/// e2e (it needs Edge installed and an interactive InPrivate launch), so this is
/// the agent-side analog of <c>--auto-join</c>: it lets the integration test
/// exercise the witness → reporter → hub → backend wiring and assert a
/// <c>TamperDetected{inprivate_opened}</c> event lands, while the title-parsing
/// itself is covered by unit tests on <see cref="InPrivateDetection"/>.
///
/// Production never passes the flag; the real <see cref="IBrowserWindowScanner"/>
/// enumerating live Edge windows is used instead.
/// </summary>
public sealed class SimulatedInPrivateScanner : IBrowserWindowScanner
{
    // A stable, obviously-synthetic handle so the monitor's per-window de-dup
    // reports it exactly once across repeated polls — proving the dedup path too.
    private static readonly BrowserWindow Window = new(
        Handle: new nint(0x1148),
        ProcessName: "msedge",
        Title: "New tab - [InPrivate] - Microsoft Edge");

    public IReadOnlyList<BrowserWindow> GetOpenBrowserWindows() => new[] { Window };
}
