using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FocusAgent.App.Tray;

internal sealed class TrayIconHost : IDisposable
{
    private readonly TaskbarIcon _icon;

    public TrayIconHost(Action onOpen, Action onQuit)
    {
        var menu = new MenuFlyout();

        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Open",
            Command = new RelayCommand(onOpen),
        });

        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Quit",
            Command = new RelayCommand(onQuit),
        });

        _icon = new TaskbarIcon
        {
            ToolTipText = "FocusAgent",
            ContextFlyout = menu,
            IconSource = new GeneratedIconSource
            {
                Text = "F",
                Foreground = new SolidColorBrush(Colors.White),
                Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x1E, 0x4F, 0x8C)),
            },
        };
    }

    public void Show() => _icon.ForceCreate();

    public void Dispose() => _icon.Dispose();
}
