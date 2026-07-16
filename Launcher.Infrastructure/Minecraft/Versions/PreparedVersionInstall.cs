/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// A composed version directory and JSON that are ready for CmlLib to inspect.
/// </summary>
internal sealed class PreparedVersionInstall(
    string versionName,
    string versionDirectory)
{
    public string VersionName { get; } = versionName;

    public Task CleanupAsync()
    {
        try
        {
            if (Directory.Exists(versionDirectory))
                Directory.Delete(versionDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return Task.CompletedTask;
    }
}

internal static class ComposedVersionInstallRunner
{
    public static async Task<string> RunAsync(
        Func<CancellationToken, Task<PreparedVersionInstall>> prepareAsync,
        Func<string, CancellationToken, Task> installFilesAsync,
        CancellationToken cancellationToken)
    {
        PreparedVersionInstall? prepared = null;
        try
        {
            prepared = await prepareAsync(cancellationToken).ConfigureAwait(false);
            await installFilesAsync(prepared.VersionName, cancellationToken).ConfigureAwait(false);
            return prepared.VersionName;
        }
        catch
        {
            if (prepared is not null)
                await prepared.CleanupAsync().ConfigureAwait(false);
            throw;
        }
    }
}
