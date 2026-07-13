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

public sealed partial class LaunchDiagnosticExportService
{
private static IReadOnlyList<LaunchDiagnosticReference> NormalizeDiagnostics(
        IReadOnlyList<LaunchDiagnosticReference> diagnostics,
        ICollection<ExportOutcome> outcomes)
    {
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<LaunchDiagnosticReference>();
        foreach (var diagnostic in diagnostics)
        {
            try
            {
                var fullPath = Path.GetFullPath(diagnostic.Path);
                if (uniquePaths.Add(fullPath))
                    normalized.Add(diagnostic with { Path = fullPath });
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
            {
                outcomes.Add(new ExportOutcome(
                    diagnostic.Type,
                    SafeFileName(diagnostic.Path),
                    null,
                    false,
                    "invalid-path",
                    0));
            }
        }

        return normalized;
    }
}
