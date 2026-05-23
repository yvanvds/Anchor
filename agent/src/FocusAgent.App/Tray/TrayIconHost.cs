using FocusAgent.Core.Realtime;
using H.NotifyIcon;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FocusAgent.App.Tray;

internal sealed class TrayIconHost : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MenuFlyoutItem _statusItem;
    private readonly DispatcherQueue _dispatcher;

    public TrayIconHost(Action onOpen, Action onQuit, DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        var menu = new MenuFlyout();

        _statusItem = new MenuFlyoutItem
        {
            Text = "Signed out",
            IsEnabled = false,
        };
        menu.Items.Add(_statusItem);

        menu.Items.Add(new MenuFlyoutSeparator());

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

    public void UpdateStatus(AgentConnectionState state, string? displayName)
    {
        var text = state switch
        {
            AgentConnectionState.Connected when !string.IsNullOrWhiteSpace(displayName) => $"Connected as {displayName}",
            AgentConnectionState.Connected => "Connected",
            AgentConnectionState.Connecting => "Connecting…",
            AgentConnectionState.Reconnecting => "Reconnecting…",
            AgentConnectionState.Disconnected => "Disconnected",
            _ => "Signed out",
        };
        _dispatcher.TryEnqueue(() =>
        {
            _statusItem.Text = text;
            _icon.ToolTipText = $"FocusAgent — {text}";
        });
    }

    public void Dispose() => _icon.Dispose();
}
