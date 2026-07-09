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
