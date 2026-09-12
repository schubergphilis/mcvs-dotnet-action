using DotNet.Testcontainers.Builders;

namespace Mcvs.Example.Tests;

/// <summary>
/// The tests of the categories that the integration, component and end-to-end
/// testing types select on. The integration test creates its dependency with
/// Testcontainers, which is the reason that a container runtime has to be
/// reachable before those testing types are run.
/// </summary>
public class SemanticVersionContainerTests
{
    private static readonly string[] OrderedTags = ["v1.0.0", "v1.0.1", "v1.1.0", "v2.0.0"];

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TheVersionThatAContainerReportsIsParsed()
    {
        await using var container = new ContainerBuilder("alpine:3.22")
            .WithEntrypoint("/bin/sh", "-c")
            .WithCommand("echo v1.2.3 && sleep infinity")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("v1.2.3"))
            .Build();

        await container.StartAsync();

        var (stdout, _) = await container.GetLogsAsync(timestampsEnabled: false);

        Assert.Equal("1.2.3", SemanticVersion.Parse(stdout.Trim()).ToString());
    }

    [Fact]
    [Trait("Category", "Component")]
    public void TheVersionOfTheActionPrecedesTheNextMajor()
    {
        var current = SemanticVersion.Parse("v1.0.0");
        var next = SemanticVersion.Parse("v2.0.0");

        Assert.True(current.IsOlderThan(next));
    }

    [Fact]
    [Trait("Category", "E2E")]
    public void TheVersionsAreOrderedFromOldToNew()
    {
        var versions = OrderedTags.Select(SemanticVersion.Parse).ToList();

        for (var index = 1; index < versions.Count; index++)
        {
            Assert.True(versions[index - 1].IsOlderThan(versions[index]));
        }
    }
}
