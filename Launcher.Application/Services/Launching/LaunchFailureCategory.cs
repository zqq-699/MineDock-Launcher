namespace Launcher.Application.Services;

public enum LaunchFailureCategory
{
    JavaVersionMismatch,
    ModDependencyMissing,
    ModVersionIncompatible,
    MissingGameFiles,
    OutOfMemory
}
