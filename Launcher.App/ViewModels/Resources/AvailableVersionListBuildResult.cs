/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.App.ViewModels.Resources;

internal sealed record AvailableVersionListBuildResult(
    IReadOnlyList<object> Items,
    int VisibleVersionCount);
