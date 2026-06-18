namespace Launcher.Application.Services;

public sealed record LaunchFailureAnalysis(
    LaunchFailureCategory Category,
    string ReasonTitle,
    string ReasonDetail,
    string Recommendation,
    int? RequiredJavaMajorVersion = null,
    int? CurrentJavaMajorVersion = null,
    string? ModName = null,
    string? DependencyName = null,
    string? MissingPath = null);
