namespace Launcher.Application;

public static class LauncherProjectLinks
{
    public const string GitHubOwner = "zqq-699";
    public const string GitHubRepositoryName = "MineDock-Launcher";
    public const string GitHubRepositoryUrl = "https://github.com/" + GitHubOwner + "/" + GitHubRepositoryName;
    public const string GitHubReleasesUrl = GitHubRepositoryUrl + "/releases";
    public const string GitHubReleasesApiUrl = "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepositoryName + "/releases";
    public const string GitHubUserAgent = "MineDock-Launcher";
    public const string GiteeRepositoryUrl = "https://gitee.com/" + GitHubOwner + "/" + GitHubRepositoryName;
    public const string GiteeUpdateManifestUrlTemplate = GiteeRepositoryUrl + "/raw/update-manifests/update/{0}/latest.json";
    public const string GitHubUpdateManifestUrlTemplate = "https://raw.githubusercontent.com/" + GitHubOwner + "/" + GitHubRepositoryName + "/update-manifests/update/{0}/latest.json";
}
