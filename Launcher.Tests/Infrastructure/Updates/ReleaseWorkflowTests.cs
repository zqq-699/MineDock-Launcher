using System.Text.Json;

namespace Launcher.Tests.Infrastructure.Updates;

public sealed class ReleaseWorkflowTests
{
    [Theory]
    [InlineData(".github/workflows/release.yml", "release")]
    [InlineData(".github/workflows/beta.yml", "beta")]
    public void WorkflowUsesTagVersionAndNotesBeforeWritingManifest(string workflowPath, string channel)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, workflowPath));

        Assert.Contains("VERSION_NAME=$versionName", text);
        Assert.Contains("notes/$versionName.md", text);
        Assert.Contains("InformationalVersion", text);
        Assert.Contains("LauncherBuildChannel", text);
        Assert.Contains("LauncherVersionCode", text);
        Assert.Contains("Write back", text);
        Assert.DoesNotContain("$versionName = $manifest.versionName", text, StringComparison.OrdinalIgnoreCase);

        var releaseIndex = text.IndexOf("Create GitHub", StringComparison.Ordinal);
        var writeBackIndex = text.IndexOf("Write back", StringComparison.Ordinal);
        Assert.True(releaseIndex >= 0);
        Assert.True(writeBackIndex > releaseIndex);
        Assert.Contains($"CHANNEL: {channel}", text);
    }

    [Theory]
    [InlineData(".github/workflows/release.yml")]
    [InlineData(".github/workflows/beta.yml")]
    public void WorkflowPublishesGiteeMirrorBeforeWritingManifest(string workflowPath)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, workflowPath));

        Assert.Contains("GITEE_OWNER: zqq-699", text);
        Assert.Contains("GITEE_REPO: MineDock-Launcher", text);
        Assert.Contains("GITEE_BRANCH: master", text);
        Assert.Contains("GITEE_TOKEN: ${{ secrets.GITEE_TOKEN }}", text);
        Assert.Contains("Create Gitee", text);
        Assert.Contains("Sync Gitee update repository and tag", text);
        Assert.Contains("update/release/latest.json", text);
        Assert.Contains("update/beta/latest.json", text);
        Assert.Contains("$generatedManifestPath = Join-Path $workspace $env:GENERATED_MANIFEST_PATH", text);
        Assert.Contains("Copy-Item $generatedManifestPath", text);
        Assert.Contains("TimeoutSec 60", text);
        Assert.Contains("attach_files?access_token=$encodedToken", text);
        Assert.Contains("curl.exe", text);
        Assert.Contains("--form\", \"file=@", text);
        Assert.DoesNotContain("--form\", \"access_token=", text);
        Assert.Contains("git tag $env:GITHUB_REF_NAME", text);
        Assert.Contains("git push origin $env:GITHUB_REF_NAME", text);

        var githubReleaseIndex = text.IndexOf("Create GitHub", StringComparison.Ordinal);
        var finalManifestIndex = text.IndexOf("Generate final", StringComparison.Ordinal);
        var syncGiteeIndex = text.IndexOf("Sync Gitee update repository and tag", StringComparison.Ordinal);
        var giteeReleaseIndex = text.IndexOf("Create Gitee", StringComparison.Ordinal);
        var githubManifestUploadIndex = text.IndexOf("Upload final manifest to GitHub", StringComparison.Ordinal);
        var writeBackIndex = text.IndexOf("Write back", StringComparison.Ordinal);

        Assert.True(githubReleaseIndex >= 0);
        Assert.True(finalManifestIndex > githubReleaseIndex);
        Assert.True(syncGiteeIndex > finalManifestIndex);
        Assert.True(giteeReleaseIndex > syncGiteeIndex);
        Assert.True(githubManifestUploadIndex > giteeReleaseIndex);
        Assert.True(writeBackIndex > githubManifestUploadIndex);
    }

    [Fact]
    public void GiteeManifestFallbackUsesMasterUpdatePath()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root.FullName, "Launcher.Application", "LauncherProjectLinks.cs"));

        Assert.Contains("GiteeRepositoryUrl + \"/raw/master/update/{0}/latest.json\"", text);
    }

    [Fact]
    public void BetaLatestManifestHasMatchingNonEmptyNotesFile()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root.FullName, "update", "beta", "latest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var versionName = document.RootElement.GetProperty("versionName").GetString();

        var notesPath = Path.Combine(root.FullName, "update", "beta", "notes", $"{versionName}.md");
        Assert.True(File.Exists(notesPath), $"Missing notes file: {notesPath}");
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(notesPath)));
    }

    [Fact]
    public void BetaManifestVersionCodeMatchesNumericMmppbbRule()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "update", "beta", "latest.json")));
        var versionName = document.RootElement.GetProperty("versionName").GetString();

        Assert.True(TryCalculateVersionCode(versionName, out var expectedVersionCode), $"Invalid beta versionName: {versionName}");
        Assert.Equal(expectedVersionCode, document.RootElement.GetProperty("versionCode").GetInt32());
    }

    [Theory]
    [InlineData("update/release/latest.json")]
    [InlineData("update/beta/latest.json")]
    public void LatestManifestUsesOnlyGithubAndGiteeDownloadSources(string manifestPath)
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, manifestPath)));
        var urls = document.RootElement
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

    private static bool TryCalculateVersionCode(string? versionName, out int versionCode)
    {
        versionCode = 0;
        if (string.IsNullOrWhiteSpace(versionName))
            return false;

        var match = System.Text.RegularExpressions.Regex.Match(
            versionName,
            @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)-beta\.(?<beta>\d+)$");
        if (!match.Success)
            return false;

        versionCode = (int.Parse(match.Groups["major"].Value) * 1_000_000)
            + (int.Parse(match.Groups["minor"].Value) * 10_000)
            + (int.Parse(match.Groups["patch"].Value) * 100)
            + int.Parse(match.Groups["beta"].Value);
        return true;
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
