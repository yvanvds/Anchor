using System.Collections.Generic;
using FocusAgent.Core.Focus;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FocusAgent.App.Focus;

public sealed partial class FocusOverlayWindow : Window
{
    private readonly IAppIdentifier _launcher;
    private readonly Action<AllowedAppRule> _onLaunched;

    public FocusOverlayWindow(IAppIdentifier launcher, Action<AllowedAppRule> onLaunched)
    {
        InitializeComponent();
        _launcher = launcher;
        _onLaunched = onLaunched;
        Title = "Anchor — Focus session";
    }

    public void UpdateContent(IReadOnlyList<AllowedAppRule> allowedRules, string? blockedAppName)
    {
        if (string.IsNullOrWhiteSpace(blockedAppName))
        {
            BlockedText.Visibility = Visibility.Collapsed;
        }
        else
        {
            BlockedText.Text = $"Just blocked: {blockedAppName}";
            BlockedText.Visibility = Visibility.Visible;
        }

        AllowedAppsPanel.Children.Clear();
        if (allowedRules.Count == 0)
        {
            AllowedAppsPanel.Children.Add(new TextBlock
            {
                Text = "No specific apps configured for this session.",
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var rule in allowedRules)
        {
            var button = new Button
            {
                Content = FormatRule(rule),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = rule,
            };
            button.Click += OnAllowedAppClicked;
            AllowedAppsPanel.Children.Add(button);
        }
    }

    private void OnAllowedAppClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AllowedAppRule rule })
            return;
        _launcher.LaunchOrActivate(rule);
        _onLaunched(rule);
    }

    private static string FormatRule(AllowedAppRule rule) => rule.MatchKind switch
    {
        AllowedAppMatchKind.ProcessName => rule.Value,
        AllowedAppMatchKind.ExecutablePath => Path.GetFileNameWithoutExtension(rule.Value),
        AllowedAppMatchKind.Publisher => $"Apps from {rule.Value}",
        _ => rule.Value,
    };
}
