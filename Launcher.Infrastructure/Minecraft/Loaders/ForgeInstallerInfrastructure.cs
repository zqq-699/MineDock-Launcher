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

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CmlLib.Core;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Minecraft;

internal interface IForgeInstallerRunner
{
    Task RunInstallerAsync(string javaCommand, string installerJarPath, string minecraftDirectory, CancellationToken cancellationToken);
}

internal interface IFinalVersionInstaller
{
    Task InstallAsync(
        string gameDirectory,
        string versionName,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond = 0);

    Task InstallAsync(
        MinecraftPath path,
        string versionName,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        return InstallAsync(
            path.BasePath,
            versionName,
            downloadSourcePreference,
            progress,
            cancellationToken,
            downloadSpeedLimitMbPerSecond);
    }

    Task InstallAsync(
        MinecraftPath path,
        string versionName,
        MinecraftDownloadOperationContext operationContext,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        return InstallAsync(
            path,
            versionName,
            downloadSourcePreference,
            progress,
            cancellationToken,
            downloadSpeedLimitMbPerSecond);
    }
}

internal sealed class FinalVersionInstaller : IFinalVersionInstaller
{
    public Task InstallAsync(
        string gameDirectory,
        string versionName,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        return InstallAsync(
            new MinecraftPath(gameDirectory),
            versionName,
            downloadSourcePreference,
            progress,
            cancellationToken,
            downloadSpeedLimitMbPerSecond);
    }

    public async Task InstallAsync(
        MinecraftPath path,
        string versionName,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        using var downloadOperation = VanillaLoaderProvider.CreateDownloadOperationContext(path);
        await InstallAsync(
            path,
            versionName,
            downloadOperation,
            downloadSourcePreference,
            progress,
            cancellationToken,
            downloadSpeedLimitMbPerSecond).ConfigureAwait(false);
    }

    public async Task InstallAsync(
        MinecraftPath path,
        string versionName,
        MinecraftDownloadOperationContext operationContext,
        DownloadSourcePreference downloadSourcePreference,
        IProgress<LauncherProgress>? progress,
        CancellationToken cancellationToken,
        int downloadSpeedLimitMbPerSecond = 0)
    {
        ArgumentNullException.ThrowIfNull(operationContext);
        var launcher = VanillaLoaderProvider.CreateLauncher(
            path,
            progress,
            downloadSourcePreference,
            downloadSpeedLimitMbPerSecond: downloadSpeedLimitMbPerSecond,
            operationContext: operationContext);
        VanillaLoaderProvider.AttachProgress(launcher, progress);
        await launcher.InstallAsync(versionName, cancellationToken);
    }
}

internal sealed class ForgeInstallerRunner : IForgeInstallerRunner
{
    internal const string InstallClientUnrecognizedOptionMessage = "'installClient' is not a recognized option";
    private static readonly TimeSpan ProcessTerminationTimeout = TimeSpan.FromSeconds(5);
    private readonly Func<ProcessStartInfo, Process?> startProcess;

    public ForgeInstallerRunner()
        : this(Process.Start)
    {
    }

    internal ForgeInstallerRunner(Func<ProcessStartInfo, Process?> startProcess)
    {
        this.startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
    }

    public async Task RunInstallerAsync(string javaCommand, string installerJarPath, string minecraftDirectory, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(javaCommand) ? "java" : javaCommand,
                Arguments = $"-jar \"{installerJarPath}\" --installClient \"{minecraftDirectory}\"",
                WorkingDirectory = minecraftDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = startProcess(startInfo)
                ?? throw new InvalidOperationException("Forge installer could not be started.");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await TerminateProcessTreeAsync(process, outputTask, errorTask).ConfigureAwait(false);
                throw;
            }

            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode == 0)
                return;

            var details = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(details)
                    ? $"Forge installer exited with code {process.ExitCode}."
                    : $"Forge installer exited with code {process.ExitCode}: {details.Trim()}");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("No usable Java runtime was found for Forge installation.", exception);
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidOperationException("No usable Java runtime was found for Forge installation.", exception);
        }
    }

    private static async Task TerminateProcessTreeAsync(
        Process process,
        Task<string> outputTask,
        Task<string> errorTask)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between HasExited and Kill.
        }
        catch (Win32Exception exception)
        {
            throw new IOException("The canceled Forge installer process tree could not be terminated.", exception);
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(ProcessTerminationTimeout)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The process was already reaped by the operating system.
        }
        catch (TimeoutException exception)
        {
            throw new IOException("The canceled Forge installer process did not terminate in time.", exception);
        }

        try
        {
            await Task.WhenAll(outputTask, errorTask)
                .WaitAsync(ProcessTerminationTimeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new IOException("The canceled Forge installer output streams did not close in time.", exception);
        }
    }
}
