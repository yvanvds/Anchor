using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace FocusAgent.App;

public static class Program
{
    private const string SingleInstanceKey = "Anchor.FocusAgent.SingleInstance";

    /// <summary>
    /// Dev-only flag: shows a fake join-confirmation toast immediately, with
    /// no WAM / hub / coordinator bootstrap, and exits after the countdown.
    /// Lets the WinUI toast path be verified end-to-end (build → launch →
    /// screenshot) without a working backend or interactive sign-in. Used by
    /// `scripts/dev/verify-toast.ps1` to validate the #41 fix.
    /// </summary>
    public const string ShowTestToastArg = "--show-test-toast";

    public static bool ShowTestToast { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        ShowTestToast = args.Any(a => string.Equals(a, ShowTestToastArg, StringComparison.OrdinalIgnoreCase));

        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Single-instance gating gets in the way of the toast-test loop (each
        // launch needs to be its own process). Skip it in that mode only.
        if (!ShowTestToast)
        {
            var keyInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
            if (!keyInstance.IsCurrent)
            {
                return 0;
            }
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });

        return 0;
    }
}
