namespace Launcher.Domain.Models;

public enum LocalResourcePackImportFailureReason
{
    None = 0,
    FileNotFound,
    UnsupportedArchive,
    UnexpectedError
}

public sealed class LocalResourcePackImportResult
{
    private LocalResourcePackImportResult(
        bool isSuccess,
        LocalResourcePackImportFailureReason failureReason,
        LocalResourcePack? importedResourcePack)
    {
        IsSuccess = isSuccess;
        FailureReason = failureReason;
        ImportedResourcePack = importedResourcePack;
    }

    public bool IsSuccess { get; }

    public LocalResourcePackImportFailureReason FailureReason { get; }

    public LocalResourcePack? ImportedResourcePack { get; }

    public static LocalResourcePackImportResult Success(LocalResourcePack resourcePack)
    {
        ArgumentNullException.ThrowIfNull(resourcePack);
        return new LocalResourcePackImportResult(true, LocalResourcePackImportFailureReason.None, resourcePack);
    }

    public static LocalResourcePackImportResult Failure(LocalResourcePackImportFailureReason failureReason)
    {
        return new LocalResourcePackImportResult(false, failureReason, null);
    }
}
