namespace Launcher.Tests.Resources;

public sealed class ResourcesRequiredDependencyPlannerTests
{
    [Theory]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("fabric-mc1.20.1-2.0.0", "1.9.0", true)]
    [InlineData("1.0.0-beta", "1.0.0", false)]
    [InlineData("not-a-version", "1.0.0", false)]
    public void DependencyVersionComparer_HandlesCommonModVersionForms(
        string installedVersion,
        string minimumVersion,
        bool expected)
    {
        Assert.Equal(
            expected,
            ResourceDependencyVersionComparer.IsGreaterThanOrEqual(installedVersion, minimumVersion));
    }

    [Fact]
    public void SelectRequiredDependencyVersion_PrefersRelease()
    {
        var beta = new ResourceProjectVersion { VersionId = "beta", VersionType = "beta" };
        var release = new ResourceProjectVersion { VersionId = "release", VersionType = "release" };

        var selected = ResourcesRequiredDependencyPlanner.SelectRequiredDependencyVersion([beta, release]);

        Assert.Same(release, selected);
    }

    [Fact]
    public void ResolveRequiredDependencyMinimumVersion_UsesDependencyVersionId()
    {
        var dependency = new ResourceProjectDependency { VersionId = "required" };
        var older = new ResourceProjectVersion { VersionId = "older", VersionNumber = "1.0.0" };
        var required = new ResourceProjectVersion { VersionId = "required", VersionNumber = "1.2.0" };

        var minimum = ResourcesRequiredDependencyPlanner.ResolveRequiredDependencyMinimumVersion(
            dependency,
            [older, required]);

        Assert.Same(required, minimum);
    }

    [Fact]
    public void ResolveDependencyRequirementState_DetectsInstalledUpdateAndMissingStates()
    {
        var dependency = new ResourceProjectDependency
        {
            Project = new ResourceProject
            {
                ProjectId = "sodium",
                Slug = "sodium"
            }
        };
        var candidate = new RequiredDependencyInstallCandidate(
            dependency,
            new ResourceProjectVersion { VersionNumber = "1.2.0" },
            new ResourceProjectVersion { VersionNumber = "1.2.0" });

        var installed = new InstalledDependencyCatalog(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sodium"] = ["1.2.1"]
        });
        var updateRequired = new InstalledDependencyCatalog(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sodium"] = ["1.1.0"]
        });
        var missing = new InstalledDependencyCatalog([]);

        Assert.Equal(
            RequiredDependencyRequirementState.Installed,
            ResourcesRequiredDependencyPlanner.ResolveDependencyRequirementState(candidate, installed));
        Assert.Equal(
            RequiredDependencyRequirementState.UpdateRequired,
            ResourcesRequiredDependencyPlanner.ResolveDependencyRequirementState(candidate, updateRequired));
        Assert.Equal(
            RequiredDependencyRequirementState.Missing,
            ResourcesRequiredDependencyPlanner.ResolveDependencyRequirementState(candidate, missing));
    }
}
