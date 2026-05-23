namespace FocusAgent.Core.Settings;

public sealed record AgentSettings(BackendSettings Backend)
{
    public const string SectionName = "";
}

public sealed record BackendSettings
{
    public const string SectionName = "Backend";

    public string BaseUrl { get; init; } = "";
    public string HubPath { get; init; } = "/hubs/session";
}
