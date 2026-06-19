namespace Launcher.Application.Services;

public sealed record LaunchDownloadDiagnostic(
    string Url,
    string DestinationPath,
    int? HttpStatusCode,
    string? LibraryName,
    string? ArtifactPath,
    string SourceKind);
