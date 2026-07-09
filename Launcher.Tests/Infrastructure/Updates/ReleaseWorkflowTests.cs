using System.Text.Json;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class ReleaseWorkflowTests
{
    [Theory]
    [InlineData(".github/workflows/release.yml", "release")]
    [InlineData(".github/workflows/beta.yml", "beta")]
    public void WorkflowUsesTagVersionAndNotesBeforeSyncingManifest(string workflowPath, string channel)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, workflowPath));

        Assert.Contains("VERSION_NAME=$versionName", text);
        Assert.Contains("notes/$versionName.md", text);
        Assert.Contains("InformationalVersion", text);
        Assert.Contains("LauncherBuildChannel", text);
        Assert.Contains("LauncherVersionCode", text);
        Assert.Contains("MANIFEST_PATH: update/latest.template.json", text);
        Assert.Contains("Sync GitHub", text);
        Assert.DoesNotContain("$versionName = $manifest.versionName", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("github.event.repository.default_branch", text);
        Assert.DoesNotContain("Write back", text);

        var finalManifestIndex = text.IndexOf("Generate final", StringComparison.Ordinal);
        var releaseIndex = text.IndexOf("Create GitHub", StringComparison.Ordinal);
        var githubManifestSyncIndex = text.IndexOf("Sync GitHub", StringComparison.Ordinal);
        Assert.True(finalManifestIndex >= 0);
        Assert.True(releaseIndex >= 0);
        Assert.True(releaseIndex > finalManifestIndex);
        Assert.True(githubManifestSyncIndex > releaseIndex);
        Assert.Contains($"CHANNEL: {channel}", text);
    }

    [Theory]
    [InlineData(".github/workflows/release.yml")]
    [InlineData(".github/workflows/beta.yml")]
    public void WorkflowPublishesManifestsToUpdateManifestBranches(string workflowPath)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, workflowPath));

        Assert.Contains("GITEE_OWNER: zqq-699", text);
        Assert.Contains("GITEE_REPO: MineDock-Launcher", text);
        Assert.Contains("GITEE_BRANCH: update-manifests", text);
        Assert.Contains("GITHUB_MANIFEST_BRANCH: update-manifests", text);
        Assert.Contains("GITEE_TOKEN: ${{ secrets.GITEE_TOKEN }}", text);
        Assert.Contains("Reserve Gitee", text);
        Assert.Contains("Create Gitee", text);
        Assert.Contains("Sync Gitee update repository and tag", text);
        Assert.Contains("Sync GitHub", text);
        Assert.Contains("Initialize-ManifestBranch", text);
        Assert.Contains("Ensure-ChannelManifest", text);
        Assert.Contains("\"update/$Channel/latest.json\"", text);
        Assert.Contains("\"update/$($env:CHANNEL)/latest.json\"", text);
        Assert.Contains("$generatedManifestPath = Join-Path $workspace $env:GENERATED_MANIFEST_PATH", text);
        Assert.Contains("Copy-Item $generatedManifestPath", text);
        Assert.Contains("git push origin HEAD:$($env:GITEE_BRANCH)", text);
        Assert.Contains("git push origin HEAD:$branch", text);
        Assert.DoesNotContain("GITEE_BRANCH: master", text);
        Assert.DoesNotContain("Copy-Item (Join-Path $workspace \"update/release/latest.json\")", text);
        Assert.DoesNotContain("Copy-Item (Join-Path $workspace \"update/beta/latest.json\")", text);
        Assert.DoesNotContain("git checkout $branch", text);

        var githubReleaseIndex = text.IndexOf("Create GitHub", StringComparison.Ordinal);
        var finalManifestIndex = text.IndexOf("Generate final", StringComparison.Ordinal);
        var reserveGiteeTagIndex = text.IndexOf("Reserve Gitee", StringComparison.Ordinal);
        var syncGiteeIndex = text.IndexOf("Sync Gitee update repository and tag", StringComparison.Ordinal);
        var syncGithubIndex = text.IndexOf("Sync GitHub", StringComparison.Ordinal);
        var giteeReleaseIndex = text.IndexOf("Create Gitee", StringComparison.Ordinal);

        Assert.True(githubReleaseIndex >= 0);
        Assert.True(finalManifestIndex >= 0);
        Assert.True(reserveGiteeTagIndex >= 0);
        Assert.True(giteeReleaseIndex > reserveGiteeTagIndex);
        Assert.True(finalManifestIndex > giteeReleaseIndex);
        var giteeManifestUploadIndex = text.IndexOf("Upload final", StringComparison.Ordinal);
        Assert.True(giteeManifestUploadIndex > finalManifestIndex);
        Assert.True(githubReleaseIndex > giteeManifestUploadIndex);
        Assert.True(syncGiteeIndex > githubReleaseIndex);
        Assert.True(syncGithubIndex > syncGiteeIndex);
    }

    [Theory]
    [InlineData(".github/workflows/release.yml")]
    [InlineData(".github/workflows/beta.yml")]
    public void WorkflowKeepsExistingGiteeReleaseSafeguards(string workflowPath)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, workflowPath));

        Assert.Contains("TimeoutSec 60", text);
        Assert.Contains("\"--max-time\", \"600\"", text);
        Assert.Contains("GITEE_FALLBACK_DOWNLOAD_URL=$giteeFallbackDownloadUrl", text);
        Assert.Contains("Resolve-GiteeDownloadUrl", text);
        Assert.Contains("Test-DownloadUrl", text);
        Assert.Contains("Invoke-WebRequest -Method Head", text);
        Assert.Contains("Range = \"bytes=0-0\"", text);
        Assert.Contains("GITEE_DOWNLOAD_URL=$giteeDownloadUrl", text);
        Assert.Contains("Gitee download URL was not resolved from the uploaded launcher asset.", text);
        Assert.Contains("Gitee final manifest upload completed.", text);
        Assert.Contains("attach_files?access_token=$encodedToken", text);
        Assert.Contains("curl.exe", text);
        Assert.Contains("--write-out", text);
        Assert.Contains("%{http_code}", text);
        Assert.Contains("Gitee upload response body:", text);
        Assert.Contains("HTTP status", text);
        Assert.Contains("--form\", \"file=@", text);
        Assert.DoesNotContain("--form\", \"access_token=", text);
        Assert.Contains("git tag $env:GITHUB_REF_NAME", text);
        Assert.Contains("git push origin $env:GITHUB_REF_NAME", text);
        Assert.Contains("git tag -f $env:GITHUB_REF_NAME", text);
        Assert.Contains("GITEE_RELEASE_ID=$($release.id)", text);
        Assert.Contains("GITHUB_RELEASE_CREATED=true", text);
        Assert.Contains("Cleanup failed", text);
        Assert.Contains("gh release delete $env:GITHUB_REF_NAME --yes", text);
        Assert.Contains("Invoke-RestMethod -Method Delete", text);
        Assert.Contains("git push origin \":refs/tags/$($env:GITHUB_REF_NAME)\"", text);
        Assert.DoesNotContain("Upload final manifest to GitHub", text);
    }

    [Fact]
    public void BetaWorkflowRejectsBetaNumbersThatWouldCollideWithReleaseVersionCode()
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, ".github/workflows/beta.yml"));

        Assert.Contains("$betaNumber = [int]$Matches.beta", text);
        Assert.Contains("$betaNumber -lt 1 -or $betaNumber -gt 98", text);
        Assert.Contains("Beta number must be between 1 and 98.", text);
        Assert.Contains("+ $betaNumber", text);
    }

    [Fact]
    public void ManifestSourcesUseUpdateManifestsBranch()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root.FullName, "Launcher.Application", "LauncherProjectLinks.cs"));

        Assert.Contains("GiteeRepositoryUrl + \"/raw/update-manifests/update/{0}/latest.json\"", text);
        Assert.Contains("\"/update-manifests/update/{0}/latest.json\"", text);
        Assert.DoesNotContain("/raw/master/update/{0}/latest.json", text);
        Assert.DoesNotContain("/master/update/{0}/latest.json", text);
    }

    [Fact]
    public void DefaultBranchUpdateDirectoryContainsOnlyNotesReadmeAndTemplate()
    {
        var root = FindRepositoryRoot();
        Assert.False(File.Exists(Path.Combine(root.FullName, "update", "release", "latest.json")));
        Assert.False(File.Exists(Path.Combine(root.FullName, "update", "beta", "latest.json")));
        Assert.True(File.Exists(Path.Combine(root.FullName, "update", "README.md")));
        Assert.True(File.Exists(Path.Combine(root.FullName, "update", "latest.template.json")));
        Assert.True(Directory.Exists(Path.Combine(root.FullName, "update", "release", "notes")));
        Assert.True(Directory.Exists(Path.Combine(root.FullName, "update", "beta", "notes")));
    }

    [Fact]
    public void ManifestTemplateUsesOnlyGithubAndGiteeDownloadSources()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "update", "latest.template.json")));
        var rootElement = document.RootElement;
        Assert.Equal(1, rootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("MineDock-Launcher", rootElement.GetProperty("appId").GetString());

        var urls = rootElement
            .GetProperty("assets")[0]
            .GetProperty("urls")
            .EnumerateArray()
            .Select(url => new
            {
                Name = url.GetProperty("name").GetString(),
                Priority = url.GetProperty("priority").GetInt32()
            })
            .ToArray();

        Assert.Equal(new[] { "gitee", "github" }, urls.Select(url => url.Name).ToArray());
        Assert.Equal(1, urls.Single(url => url.Name == "gitee").Priority);
        Assert.Equal(2, urls.Single(url => url.Name == "github").Priority);
        Assert.DoesNotContain(urls, url => url.Name is "oss" or "gitcode");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.GetFiles("Launcher.sln", SearchOption.TopDirectoryOnly).Length == 1)
                return directory;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
