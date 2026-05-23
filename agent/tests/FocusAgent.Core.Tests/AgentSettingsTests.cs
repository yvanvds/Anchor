using FocusAgent.Core.Logging;
using FocusAgent.Core.Settings;

namespace FocusAgent.Core.Tests;

public class AgentSettingsTests
{
    [Fact]
    public void BackendSettings_DefaultsHubPathToSessionHub()
    {
        var backend = new BackendSettings();

        Assert.Equal("/hubs/session", backend.HubPath);
    }

    [Fact]
    public void BackendSettings_RoundTripsBaseUrl()
    {
        var backend = new BackendSettings { BaseUrl = "https://anchor.example/" };

        Assert.Equal("https://anchor.example/", backend.BaseUrl);
    }

    [Fact]
    public void AgentLogPaths_PointsAtLocalAppDataAnchorFocusAgentLogs()
    {
        var path = AgentLogPaths.LocalAppDataLogDirectory();

        Assert.EndsWith(Path.Combine("Anchor", "FocusAgent", "logs"), path);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            path);
    }
}
