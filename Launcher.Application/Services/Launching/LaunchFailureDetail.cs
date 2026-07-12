/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

public sealed record LaunchFailureDetail(
    LaunchFailureDetailKind Kind,
    string? ModName = null,
    string? ModVersion = null,
    string? DependencyName = null,
    string? RequiredVersion = null,
    string? CurrentVersion = null,
    string? OriginalReason = null,
    string? OriginalSuggestion = null);

public enum LaunchFailureDetailKind
{
    MissingDependency,
    IncompatibleDependencyVersion,
    IncompatibleMinecraftVersion,
    IncompatibleLoaderVersion,
    ModConflict,
    General
}
