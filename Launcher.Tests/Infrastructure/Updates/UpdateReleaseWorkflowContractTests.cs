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
