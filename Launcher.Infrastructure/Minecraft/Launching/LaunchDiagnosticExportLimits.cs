/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Launcher.Application.Services;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Logging;

namespace Launcher.Infrastructure.Minecraft;

internal sealed record LaunchDiagnosticExportLimits(
    long MaxSourceFileBytes,
    long MaxTotalSourceBytes)
{
    public static LaunchDiagnosticExportLimits Default { get; } = new(
        128L * 1024 * 1024,
        256L * 1024 * 1024);
}
