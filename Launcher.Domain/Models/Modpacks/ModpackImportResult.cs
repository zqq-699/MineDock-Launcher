namespace Launcher.Domain.Models;

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
        bool isSuccess,
        ModpackImportFailureReason failureReason,
        GameInstance? importedInstance)
    {
        IsSuccess = isSuccess;
        FailureReason = failureReason;
        ImportedInstance = importedInstance;
    }

    public bool IsSuccess { get; }

    public ModpackImportFailureReason FailureReason { get; }

    public GameInstance? ImportedInstance { get; }

    public static ModpackImportResult Success(GameInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return new ModpackImportResult(true, ModpackImportFailureReason.None, instance);
    }

    public static ModpackImportResult Failure(ModpackImportFailureReason failureReason)
    {
        return new ModpackImportResult(false, failureReason, null);
    }
}
