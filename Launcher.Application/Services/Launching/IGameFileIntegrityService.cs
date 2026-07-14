/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Diagnostics;
using Launcher.Domain.Models;

namespace Launcher.Application.Services;

/// <summary>
/// Validates the files required by a resolved Minecraft launch plan and, when
/// explicitly allowed, repairs files for which trusted recovery metadata exists.
/// </summary>
public interface IGameFileIntegrityService
{
    Task<GameFileRepairResult> ValidateAndRepairAsync(
        GameFileIntegrityRequest request,
        GameFileRepairOptions options,
        IProgress<LauncherProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the concrete file paths emitted by the non-started Java
    /// process. Callers must invoke this immediately before Process.Start().
    /// </summary>
    Task<GameFileRepairResult> ValidateFinalLaunchCommandAsync(
        GameFileIntegrityRequest request,
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken = default);
}

public sealed record GameFileIntegrityRequest(
    string MinecraftDirectory,
    string VersionName,
    string InstanceDirectory,
    DownloadSourcePreference DownloadSourcePreference = DownloadSourcePreference.Auto,
    int DownloadSpeedLimitMbPerSecond = 0);

public sealed record GameFileRepairOptions(bool AllowRepair);

public enum GameFileRepairFailureReason
{
    None,
    Missing,
    Corrupted,
    MetadataIncomplete,
    DownloadFailed,
    ProcessorRegenerationFailed,
    PublicationFailed,
    FinalLaunchPlanInvalid,
    Canceled
}

public enum GameFileVerificationLevel
{
    HashVerified,
    SizeVerified,
    StructureVerified,
    ExistenceOnly,
    Unverifiable
}

public sealed record GameFileRepairFailure(
    string TargetPath,
    string Category,
    GameFileRepairFailureReason Reason,
    string RecoveryMethod,
    string? Source);

public sealed record GameFileRepairResult(
    bool LaunchAllowed,
    int RequiredCount,
    int MissingCount,
    int CorruptedCount,
    int UnverifiableCount,
    int RepairableCount,
    int RepairedCount,
    int FailedCount,
    IReadOnlyList<GameFileRepairFailure> Failures)
{
    public static GameFileRepairResult Empty { get; } = new(
        LaunchAllowed: true,
        RequiredCount: 0,
        MissingCount: 0,
        CorruptedCount: 0,
        UnverifiableCount: 0,
        RepairableCount: 0,
        RepairedCount: 0,
        FailedCount: 0,
        Failures: []);
}
