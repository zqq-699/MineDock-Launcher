/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Services;

public sealed class LaunchProcessExitedException : Exception
{
    public LaunchProcessExitedException(string? diagnosticPath, Exception? innerException = null)
        : base("The launched Minecraft process exited before startup completed.", innerException)
    {
        Report = new LaunchFailureReport(
            LaunchFailureKind.StartupAbnormalExit,
            string.Empty,
            string.Empty,
            null,
            diagnosticPath,
            string.IsNullOrWhiteSpace(diagnosticPath) ? null : Path.GetDirectoryName(diagnosticPath));
    }

    public LaunchProcessExitedException(LaunchFailureReport report, Exception? innerException = null)
        : base("The launched Minecraft process exited before startup completed.", innerException)
    {
        Report = report;
    }

    public LaunchFailureReport Report { get; }

    public string? DiagnosticPath => Report.DiagnosticPath;
}
