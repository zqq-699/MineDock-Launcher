/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

public sealed class GameInstanceMutationConflictException(
    string expectedInstanceId,
    string versionName)
    : InvalidOperationException(
        $"The instance at version '{versionName}' no longer matches instance '{expectedInstanceId}'.")
{
    public string ExpectedInstanceId { get; } = expectedInstanceId;

    public string VersionName { get; } = versionName;
}
