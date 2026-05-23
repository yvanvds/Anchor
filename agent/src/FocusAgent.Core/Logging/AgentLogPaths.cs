namespace FocusAgent.Core.Logging;

public static class AgentLogPaths
{
    public static string LocalAppDataLogDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Anchor", "FocusAgent", "logs");
}
