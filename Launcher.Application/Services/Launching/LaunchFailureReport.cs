namespace Launcher.Application.Services;

public sealed record LaunchFailureReport(
    LaunchFailureKind Kind,
    string InstanceName,
    string VersionName,
    int? ExitCode,
    string? DiagnosticPath,
    string? DiagnosticDirectory,
    LaunchFailureAnalysis? Analysis = null);
