using FocusAgent.Core.Tamper;

namespace FocusAgent.Core.Tests;

public class InPrivateDetectionTests
{
    [Theory]
    // The window-title forms Edge uses for an InPrivate window. "InPrivate" is a
    // brand term Edge keeps in English across locales, so the marker is stable.
    [InlineData("New tab - [InPrivate] - Microsoft Edge")]
    [InlineData("Some site and 2 more pages - [InPrivate] - Microsoft Edge")]
    [InlineData("inprivate")] // case-insensitive
    public void Recognises_an_edge_inprivate_window(string title)
    {
        Assert.True(InPrivateDetection.IsInPrivateEdgeWindow("msedge", title));
    }

    [Fact]
    public void Ignores_an_ordinary_edge_window()
    {
        // The non-InPrivate title carries the profile label, never "InPrivate".
        Assert.False(InPrivateDetection.IsInPrivateEdgeWindow(
            "msedge", "Anchor - Personal - Microsoft Edge"));
    }

    [Fact]
    public void Matches_the_edge_process_case_insensitively()
    {
        // QueryFullProcessImageName can hand back either casing.
        Assert.True(InPrivateDetection.IsInPrivateEdgeWindow(
            "MSEDGE", "New tab - [InPrivate] - Microsoft Edge"));
    }

    [Fact]
    public void Scopes_the_marker_to_the_edge_process()
    {
        // A non-Edge window whose own title happens to contain "InPrivate" (e.g.
        // a docs page opened in another browser) must not trip the agent witness.
        Assert.False(InPrivateDetection.IsInPrivateEdgeWindow(
            "chrome", "What is InPrivate browsing? - Google Chrome"));
        Assert.False(InPrivateDetection.IsInPrivateEdgeWindow(
            "notepad", "inprivate-notes.txt - Notepad"));
    }

    [Theory]
    [InlineData(null, "New tab - [InPrivate] - Microsoft Edge")]
    [InlineData("", "New tab - [InPrivate] - Microsoft Edge")]
    [InlineData("msedge", null)]
    [InlineData("msedge", "")]
    [InlineData("msedge", "   ")]
    public void Returns_false_for_missing_inputs(string? processName, string? title)
    {
        Assert.False(InPrivateDetection.IsInPrivateEdgeWindow(processName, title));
    }
}
