/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Serilog.Core;
using Serilog.Events;

namespace Launcher.App.Logging;

public interface ILauncherLogLevelController
{
    bool IsDiagnosticLoggingEnabled { get; }

    void SetDiagnosticLoggingEnabled(bool enabled);
}

internal sealed class LauncherLogLevelController : ILauncherLogLevelController
{
    public LauncherLogLevelController(bool enableDiagnosticLogging)
    {
        LevelSwitch = new LoggingLevelSwitch(ResolveMinimumLevel(enableDiagnosticLogging));
        MicrosoftLevelSwitch = new LoggingLevelSwitch(ResolveMicrosoftMinimumLevel(enableDiagnosticLogging));
        IsDiagnosticLoggingEnabled = enableDiagnosticLogging;
    }

    public bool IsDiagnosticLoggingEnabled { get; private set; }

    internal LoggingLevelSwitch LevelSwitch { get; }

    internal LoggingLevelSwitch MicrosoftLevelSwitch { get; }

    public void SetDiagnosticLoggingEnabled(bool enabled)
    {
        IsDiagnosticLoggingEnabled = enabled;
        LevelSwitch.MinimumLevel = ResolveMinimumLevel(enabled);
        MicrosoftLevelSwitch.MinimumLevel = ResolveMicrosoftMinimumLevel(enabled);
    }

    internal static LogEventLevel ResolveMinimumLevel(bool enabled) =>
        enabled ? LogEventLevel.Verbose : LogEventLevel.Information;

    private static LogEventLevel ResolveMicrosoftMinimumLevel(bool enabled) =>
        enabled ? LogEventLevel.Verbose : LogEventLevel.Warning;
}
