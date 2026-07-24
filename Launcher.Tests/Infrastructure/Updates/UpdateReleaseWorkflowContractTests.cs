namespace Launcher.Tests.Infrastructure.Updates;

public sealed class UpdateReleaseWorkflowContractTests
{
    [Theory]
    [InlineData("release.yml")]
    [InlineData("beta.yml")]
    public void WorkflowPublishesUnsignedManifestAndRemovesLegacySignatureSidecars(string fileName)
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root.FullName, ".github", "workflows", fileName));

        Assert.Contains(".Replace(\"`r`n\", \"`n\")", workflow, StringComparison.Ordinal);
        Assert.Contains("Verify published manifests are byte-identical", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "Receive-VerifiedCopy \"GitHub\" \"latest.json\" \"$githubBase/latest.json\"",
            workflow,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            Count(workflow, "Remove-Item \"update/release/latest.json.sig\", \"update/beta/latest.json.sig\""));
        Assert.DoesNotContain("environment: release-signing", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE_SIGNING", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Launcher.UpdateSigning", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("GENERATED_SIGNATURE", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed-latest", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SHA_PATH", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("$assetName.sha256", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ensure-ChannelManifest", workflow, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("release.yml")]
    [InlineData("beta.yml")]
    public void WorkflowEmbedsMcresBhlApiKeyBeforePublishing(string fileName)
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root.FullName, ".github", "workflows", fileName));

        var secretMapping = workflow.IndexOf(
            "BHL_API_KEY: ${{ secrets.BHL_API_KEY }}",
            StringComparison.Ordinal);
        var keyWrite = workflow.IndexOf(
            "Set-Content -Path \".local-secrets/mcres-bhl.key\" -Value $env:BHL_API_KEY.Trim() -NoNewline -Encoding utf8",
            StringComparison.Ordinal);
        var publish = workflow.IndexOf(
            "dotnet publish Launcher.App/Launcher.App.csproj",
            StringComparison.Ordinal);

        Assert.True(secretMapping >= 0);
        Assert.True(keyWrite > secretMapping);
        Assert.True(publish > keyWrite);
        Assert.Contains(
            "Repository secret BHL_API_KEY is not configured.",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ManualBuildWorkflowUploadsArtifactWithoutPublishingOrTagging()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root.FullName,
            ".github",
            "workflows",
            "manual-build.yml"));

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: read", workflow, StringComparison.Ordinal);
        Assert.Contains("BHL_API_KEY: ${{ secrets.BHL_API_KEY }}", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "Set-Content -Path \".local-secrets/mcres-bhl.key\"",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "dotnet publish Launcher.App/Launcher.App.csproj -c Release -p:PublishProfile=WinX64FrameworkDependentSingleFile",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@v4", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh release", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git tag", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("git push", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GITEE_TOKEN", workflow, StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Launcher.sln")))
            directory = directory.Parent;
        return directory ?? throw new DirectoryNotFoundException("Could not locate the launcher repository root.");
    }
}
