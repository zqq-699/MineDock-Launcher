namespace Launcher.Tests.Infrastructure.Updates;

public sealed class UpdateReleaseWorkflowContractTests
{
    [Theory]
    [InlineData("release.yml")]
    [InlineData("beta.yml")]
    public void WorkflowPublishesOneSignedManifestPairWithoutChecksumSidecar(string fileName)
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root.FullName, ".github", "workflows", fileName));

        Assert.Contains("environment: release-signing", workflow, StringComparison.Ordinal);
        Assert.Equal(1, Count(workflow, "${{ secrets.UPDATE_SIGNING_PRIVATE_KEY_BASE64 }}"));
        Assert.Contains("derive-public --private-pem .local-secrets/update-signing-private.pem", workflow, StringComparison.Ordinal);
        Assert.Contains("--signature $env:GENERATED_SIGNATURE_PATH", workflow, StringComparison.Ordinal);
        Assert.Contains(".Replace(\"`r`n\", \"`n\")", workflow, StringComparison.Ordinal);
        Assert.Contains("latest.json.sig", workflow, StringComparison.Ordinal);
        Assert.Contains("Verify published signed manifests are byte-identical", workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);
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
