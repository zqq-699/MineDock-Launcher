namespace Launcher.Domain.Models;

public enum ModpackImportStatus
{
    Success = 0,
    PartialSuccess,
    Failure
}

public enum ModpackImportFailureReason
{
    None = 0,
    FileNotFound,
    UnsupportedArchive,
    InvalidManifest,
    UnsupportedLoader,
    MissingCurseForgeApiKey,
    CurseForgeFileUnavailable,
    HashMismatch,
    UnexpectedError
}

public sealed class ModpackImportResult
{
    private ModpackImportResult(
        ModpackImportStatus status,
        ModpackImportFailureReason failureReason,
        GameInstance? importedInstance,
        IReadOnlyList<ManualModpackDownload>? manualDownloads)
    {
        Status = status;
        FailureReason = failureReason;
        ImportedInstance = importedInstance;
        ManualDownloads = manualDownloads ?? [];
    }

    public ModpackImportStatus Status { get; }

    public bool IsSuccess => Status is not ModpackImportStatus.Failure;

    public bool IsPartialSuccess => Status is ModpackImportStatus.PartialSuccess;

    public ModpackImportFailureReason FailureReason { get; }

    public GameInstance? ImportedInstance { get; }

    public IReadOnlyList<ManualModpackDownload> ManualDownloads { get; }

    public bool HasManualDownloads => ManualDownloads.Count > 0;

    public static ModpackImportResult Success(
        GameInstance instance,
        IReadOnlyList<ManualModpackDownload>? manualDownloads = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return new ModpackImportResult(ModpackImportStatus.Success, ModpackImportFailureReason.None, instance, manualDownloads);
    }

    public static ModpackImportResult PartialSuccess(
        GameInstance instance,
        IReadOnlyList<ManualModpackDownload> manualDownloads)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(manualDownloads);
        return new ModpackImportResult(ModpackImportStatus.PartialSuccess, ModpackImportFailureReason.None, instance, manualDownloads);
    }

    public static ModpackImportResult Failure(ModpackImportFailureReason failureReason)
    {
        return new ModpackImportResult(ModpackImportStatus.Failure, failureReason, null, null);
    }
}
