namespace FocusAgent.Core.Tamper;

/// <summary>
/// Sub-kinds the agent reports inside a <c>TamperDetected</c> event's payload
/// (<c>{"kind":"…"}</c>), reported via the hub's <c>ReportEvent</c> exactly like
/// the extension's in-browser kinds (#105). The backend stores and surfaces the
/// string verbatim — there is no server-side enum to keep in sync — and the
/// dashboard's tamper flag (#105) counts any <c>TamperDetected</c> regardless of
/// sub-kind, so a new kind here surfaces with no backend or dashboard change.
/// </summary>
public static class TamperKinds
{
    /// <summary>
    /// The extension was disabled or removed during an active session (#146
    /// part 1). The agent is the on-box witness: it observes the native-messaging
    /// link drop (the browser tears the host down when the extension goes away)
    /// and reports this kind, because the extension cannot witness its own
    /// disablement.
    /// </summary>
    public const string ExtensionDisabled = "extension_disabled";

    /// <summary>
    /// An Edge InPrivate window was open during an active session (#148). The
    /// extension only ever sees InPrivate windows once it has been allowed in
    /// InPrivate — which is also the case where it still filters them — so the
    /// reliable signal is the agent's: it enumerates Edge windows and recognises
    /// an InPrivate one by its title even when the extension has no incognito
    /// access (see <c>classifyCreatedWindow</c> in extension/src/shared/tamper.ts).
    /// Shares the extension's existing in-browser kind string so one dashboard
    /// flag covers both witnesses.
    /// </summary>
    public const string InPrivateOpened = "inprivate_opened";
}
