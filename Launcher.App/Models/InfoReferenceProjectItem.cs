/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Text.Json.Serialization;

namespace Launcher.App.Models;

public sealed record InfoReferenceProjectItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("copyrightNotice")] string CopyrightNotice,
    [property: JsonPropertyName("projectUrl")] string ProjectUrl,
    [property: JsonPropertyName("licenseText")] string LicenseText);
