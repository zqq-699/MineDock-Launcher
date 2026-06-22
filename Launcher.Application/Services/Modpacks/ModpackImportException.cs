using Launcher.Domain.Models;

namespace Launcher.Application.Services;

public sealed class ModpackImportException : Exception
{
    public ModpackImportException(ModpackImportFailureReason failureReason)
    {
        FailureReason = failureReason;
    }

    public ModpackImportException(ModpackImportFailureReason failureReason, string message)
        : base(message)
    {
        FailureReason = failureReason;
    }

    public ModpackImportException(ModpackImportFailureReason failureReason, string message, Exception innerException)
        : base(message, innerException)
    {
        FailureReason = failureReason;
    }

    public ModpackImportFailureReason FailureReason { get; }
}
