/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Domain.Models;

public sealed record ResourceProjectReference(
    ResourceProjectKind Kind,
    ResourceProjectSource Source,
    string ProjectId);
