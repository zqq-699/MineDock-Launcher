using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public interface IModpackExportService
{
    Task<ModpackExportResult> ExportAsync(
        ModpackExportRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ModpackExportRequest(
    GameInstance Instance,
    ModpackExportKind Kind,
    string Name,
    string Author,
    string Version,
    string OutputArchivePath,
    bool IncludeMods,
    bool IncludeDisabledMods,
    bool IncludeResourcePacks,
    bool IncludeShaderPacks,
    bool IncludeConfig);

public sealed record ModpackExportResult(
    bool IsSuccess,
    ModpackExportFailureReason FailureReason = ModpackExportFailureReason.None,
    string? OutputArchivePath = null,
    int ManifestFileCount = 0,
    int OverrideFileCount = 0);

public enum ModpackExportKind
{
    CurseForge,
    Modrinth
}

public enum ModpackExportFailureReason
{
    None,
    UnsupportedType,
    InvalidRequest,
    MissingCurseForgeApiKey,
    MissingLoaderVersion,
    CurseForgeApiFailed,
    ModrinthApiFailed,
    FileSystemError,
    UnexpectedError
}
