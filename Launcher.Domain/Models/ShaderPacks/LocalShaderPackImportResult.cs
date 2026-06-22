namespace Launcher.Domain.Models;

public enum LocalShaderPackImportFailureReason
{
    None = 0,
    FileNotFound,
    UnsupportedArchive,
    UnexpectedError
}

public sealed class LocalShaderPackImportResult
{
    private LocalShaderPackImportResult(
        bool isSuccess,
        LocalShaderPackImportFailureReason failureReason,
        LocalShaderPack? importedShaderPack)
    {
        IsSuccess = isSuccess;
        FailureReason = failureReason;
        ImportedShaderPack = importedShaderPack;
    }

    public bool IsSuccess { get; }

    public LocalShaderPackImportFailureReason FailureReason { get; }

    public LocalShaderPack? ImportedShaderPack { get; }

    public static LocalShaderPackImportResult Success(LocalShaderPack shaderPack)
    {
        ArgumentNullException.ThrowIfNull(shaderPack);
        return new LocalShaderPackImportResult(true, LocalShaderPackImportFailureReason.None, shaderPack);
    }

    public static LocalShaderPackImportResult Failure(LocalShaderPackImportFailureReason failureReason)
    {
        return new LocalShaderPackImportResult(false, failureReason, null);
    }
}
