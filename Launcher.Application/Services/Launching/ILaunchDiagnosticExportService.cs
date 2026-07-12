/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

public interface ILaunchDiagnosticExportService
{
    Task<LaunchDiagnosticExportResult> ExportAsync(
        LaunchDiagnosticExportRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record LaunchDiagnosticExportRequest(
    string OutputArchivePath,
    string InstanceName,
    string VersionName,
    IReadOnlyList<LaunchDiagnosticReference> Diagnostics);

public sealed record LaunchDiagnosticExportResult(
    bool IsSuccess,
    LaunchDiagnosticExportFailureReason FailureReason = LaunchDiagnosticExportFailureReason.None,
    string? OutputArchivePath = null,
    int ExportedFileCount = 0,
    int SkippedFileCount = 0);

public enum LaunchDiagnosticExportFailureReason
{
    None,
    NoReadableDiagnostics,
    FileSystemError,
    UnexpectedError
}
