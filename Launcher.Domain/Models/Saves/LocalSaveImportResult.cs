namespace Launcher.Domain.Models;

public enum LocalSaveImportFailureReason
{
    None = 0,
    FileNotFound,
    InvalidMinecraftSaveArchive,
    UnsupportedArchive,
    UnexpectedError
}

public sealed class LocalSaveImportResult
{
    private LocalSaveImportResult(
        bool isSuccess,
        LocalSaveImportFailureReason failureReason,
        LocalSave? importedSave)
    {
        IsSuccess = isSuccess;
        FailureReason = failureReason;
        ImportedSave = importedSave;
    }

    public bool IsSuccess { get; }

    public LocalSaveImportFailureReason FailureReason { get; }

    public LocalSave? ImportedSave { get; }

    public static LocalSaveImportResult Success(LocalSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return new LocalSaveImportResult(true, LocalSaveImportFailureReason.None, save);
    }

    public static LocalSaveImportResult Failure(LocalSaveImportFailureReason failureReason)
    {
        return new LocalSaveImportResult(false, failureReason, null);
    }
}
