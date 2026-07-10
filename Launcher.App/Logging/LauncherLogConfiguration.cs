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

using System.IO;
using Launcher.Application;
using Serilog;
using Serilog.Events;

namespace Launcher.App.Logging;

internal static class LauncherLogConfiguration
{
    public const int RetainedDays = 30;
    public const long FileSizeLimitBytes = 20 * 1024 * 1024;
    public const bool RollOnFileSizeLimit = true;
    public const string LogFileNamePattern = "bhl-.log";

    private const string LogDirectoryName = "log";
    private const string LogFileSearchPattern = "bhl*.log";

    public static Serilog.ILogger CreateLogger()
    {
        var logDirectory = ResolveLogDirectory();
        Directory.CreateDirectory(logDirectory);
        PruneOldLogFiles(logDirectory, DateTimeOffset.Now);

        var logPath = Path.Combine(logDirectory, LogFileNamePattern);
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: null,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: RollOnFileSizeLimit,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public static string ResolveLogDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, LauncherApplicationIdentity.StorageDirectoryName, LogDirectoryName);
    }

    public static void PruneOldLogFiles(string logDirectory, DateTimeOffset now)
    {
        if (!Directory.Exists(logDirectory))
            return;

        var cutoff = now.AddDays(-RetainedDays).UtcDateTime;
        foreach (var path in Directory.EnumerateFiles(logDirectory, LogFileSearchPattern, SearchOption.TopDirectoryOnly))
            DeleteIfExpired(path, cutoff);
    }

    private static void DeleteIfExpired(string path, DateTime cutoff)
    {
        try
        {
            var lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            if (lastWriteTimeUtc < cutoff)
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
