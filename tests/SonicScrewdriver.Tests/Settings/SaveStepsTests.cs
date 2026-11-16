using System.IO;
using SonicScrewdriver.Settings;

namespace SonicScrewdriver.Tests.Settings;

/// <summary>Second review, finding 3: the JSON save never depends on the registry write.</summary>
public class SaveStepsTests
{
    [Fact]
    public void A_registry_failure_does_not_prevent_the_file_save_and_is_reported()
    {
        var saved = false;
        var problems = SaveSteps.Run(() => saved = true, () => throw new UnauthorizedAccessException("denied"));
        Assert.True(saved);
        var message = Assert.Single(problems);
        Assert.Contains("denied", message);
        Assert.Contains("Start with Windows", message);
    }

    [Fact]
    public void The_file_is_saved_before_the_registry_is_touched()
    {
        var order = new List<string>();
        SaveSteps.Run(() => order.Add("file"), () => order.Add("registry"));
        Assert.Equal(new[] { "file", "registry" }, order);
    }

    [Fact]
    public void A_file_failure_is_reported_and_the_registry_step_still_runs()
    {
        var registry = false;
        var problems = SaveSteps.Run(() => throw new IOException("locked"), () => registry = true);
        Assert.True(registry);
        Assert.Contains("locked", Assert.Single(problems));
    }

    [Fact]
    public void No_registry_step_and_no_failure_gives_no_message()
    {
        Assert.Empty(SaveSteps.Run(() => { }, null));
    }
}
