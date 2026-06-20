namespace Launcher.Application.Services;

public sealed record LaunchDownloadDiagnostic(
    string OriginalUrl,
    string ActualUrl,
    string DestinationPath,
    int? HttpStatusCode,
    string? LibraryName,
    string? ArtifactPath,
    string RequestedSourcePreference,
    string ResolvedSourceKind,
    string ResourceCategory);
