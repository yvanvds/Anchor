using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace FocusAgent.App;

public static class Program
{
    private const string SingleInstanceKey = "Anchor.FocusAgent.SingleInstance";

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var keyInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
        if (!keyInstance.IsCurrent)
        {
            return 0;
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
