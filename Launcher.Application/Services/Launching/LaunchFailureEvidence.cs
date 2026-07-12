/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

public sealed record LaunchFailureEvidence(
    LaunchFailureEvidenceKind Kind,
    string Text);

public enum LaunchFailureEvidenceKind
{
    Reason,
    Suggestion
}
