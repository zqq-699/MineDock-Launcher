namespace Launcher.Tests.Resources;

public sealed class ResourcesAvailableVersionListBuilderTests
{
    [Fact]
    public void MatchesFilters_UsesSearchVersionAndLoader()
    {
        var builder = new ResourcesAvailableVersionListBuilder(CreateOptions(showsLoaderFilters: true));
        var version = new ResourceProjectVersion
        {
            Name = "Sodium",
            VersionNumber = "0.5.8",
            FileName = "sodium-fabric-mc1.20.1.jar",
            VersionType = "release",
            GameVersions = ["1.20.1"],
            Loaders = ["fabric"]
        };

        Assert.True(builder.MatchesFilters(version, "1.20.1", "fabric", "sod"));
        Assert.False(builder.MatchesFilters(version, "1.19.4", "fabric", "sod"));
        Assert.False(builder.MatchesFilters(version, "1.20.1", "forge", "sod"));
        Assert.False(builder.MatchesFilters(version, "1.20.1", "fabric", "iris"));
    }

    [Fact]
    public void Build_GroupsVersionsByCompatibility()
    {
        var builder = new ResourcesAvailableVersionListBuilder(CreateOptions(showsLoaderFilters: true));
        var versions = new[]
        {
            new ResourceProjectVersion
            {
                Name = "Fabric build",
                GameVersions = ["1.20.1"],
                Loaders = ["fabric"]
            },
            new ResourceProjectVersion
            {
                Name = "Forge build",
                GameVersions = ["1.20.1"],
                Loaders = ["forge"]
            }
        };

        var result = builder.Build(
            versions,
            "All versions",
            selectedProject: null,
            fallbackIconKey: "fallback",
            selectedVersionId: "all",
            selectedLoaderId: "all",
            searchQuery: string.Empty);

        var headers = result.Items.OfType<ResourcesModVersionListHeaderItem>().Select(item => item.Title).ToArray();
        Assert.Equal(2, result.VisibleVersionCount);
        Assert.Contains("1.20.1-fabric", headers);
        Assert.Contains("1.20.1-forge", headers);
    }

    [Fact]
    public void Append_InsertsVersionsIntoExistingCompatibilityGroups()
    {
        var builder = new ResourcesAvailableVersionListBuilder(CreateOptions(showsLoaderFilters: true));
        var items = new List<object>
        {
            new ResourcesModVersionListHeaderItem("1.20.1-fabric"),
            new ResourcesModVersionItemViewModel(new ResourceProjectVersion
            {
                Name = "Existing",
                GameVersions = ["1.20.1"],
                Loaders = ["fabric"]
            }, project: null)
        };

        var appendedCount = builder.Append(
            items,
            [
                new ResourceProjectVersion
                {
                    Name = "New",
                    GameVersions = ["1.20.1"],
                    Loaders = ["fabric"]
                }
            ],
            "All versions",
            selectedProject: null,
            fallbackIconKey: "fallback",
            selectedVersionId: "all",
            selectedLoaderId: "all",
            searchQuery: string.Empty,
            currentVisibleCount: 1);

        Assert.Equal(1, appendedCount);
        Assert.IsType<ResourcesModVersionItemViewModel>(items[2]);
    }

    private static ResourcesOnlineProjectPageOptions CreateOptions(bool showsLoaderFilters)
    {
        return new ResourcesOnlineProjectPageOptions(
            ResourceProjectKind.Mod,
            "Mods",
            "fallback",
            showsLoaderFilters,
            "All versions",
            "All loaders",
            "Loading",
            "Empty",
            "Load error",
            "Loading more",
            "No more",
            "Load more error",
            "Missing API key",
            "Info",
            "Target",
            "Local",
            "Targets loading",
            "Targets error",
            "Versions loading",
            "Versions empty",
            "Versions local empty",
            "Versions filter empty",
            "Versions load error",
            "Versions loading more",
            "Versions no more",
            "Versions load more error",
            "All versions",
            "Pick folder",
            "Downloading",
            "Downloading {0}",
            "Downloaded {0}",
            "Download failed",
            "Installed {0}",
            "Install failed",
            "File exists {0}",
            []);
    }
}
