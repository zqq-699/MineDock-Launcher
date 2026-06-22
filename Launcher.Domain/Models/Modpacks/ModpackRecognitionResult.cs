namespace Launcher.Domain.Models;

public enum ModpackRecognitionFailureReason
{
    None = 0,
    FileNotFound,
    UnsupportedArchive,
    InvalidManifest,
    UnsupportedLoader,
    UnexpectedError
}

public sealed class ModpackRecognitionResult
{
    private ModpackRecognitionResult(
        bool isSuccess,
        ModpackRecognitionFailureReason failureReason)
    {
        IsSuccess = isSuccess;
        FailureReason = failureReason;
    }

    public bool IsSuccess { get; }

    public ModpackRecognitionFailureReason FailureReason { get; }

    public static ModpackRecognitionResult Success()
    {
        return new ModpackRecognitionResult(true, ModpackRecognitionFailureReason.None);
    }

    public static ModpackRecognitionResult Failure(ModpackRecognitionFailureReason failureReason)
    {
        return new ModpackRecognitionResult(false, failureReason);
    }
}
