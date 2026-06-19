using Launcher.Application.Services;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record LaunchDiagnosticResult(
    string? DiagnosticPath,
    LaunchFailureAnalysis? Analysis,
    string? FailureSummary = null);
