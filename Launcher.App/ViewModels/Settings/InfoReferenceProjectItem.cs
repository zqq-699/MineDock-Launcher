/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.App.ViewModels.Settings;

public sealed record InfoReferenceProjectItem(
    string Name,
    string Version,
    string Url,
    string CopyrightNotice,
    string LicenseText);
