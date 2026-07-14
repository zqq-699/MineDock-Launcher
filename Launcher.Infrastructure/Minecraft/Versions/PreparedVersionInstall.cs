/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// A version JSON that is ready for CmlLib to inspect while its independent
/// client archive download continues in parallel.
/// </summary>
internal sealed class PreparedVersionInstall(
    string versionName,
    string versionDirectory,
    Task clientJarDownload)
{
    public string VersionName { get; } = versionName;
    public Task ClientJarDownload { get; } = clientJarDownload;

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
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        PreparedVersionInstall? prepared = null;
        Task? installFilesTask = null;
        try
        {
            prepared = await prepareAsync(linkedCancellation.Token).ConfigureAwait(false);
            installFilesTask = installFilesAsync(prepared.VersionName, linkedCancellation.Token);
            await AwaitTogetherOrCancelAsync(prepared.ClientJarDownload, installFilesTask, linkedCancellation)
                .ConfigureAwait(false);
            return prepared.VersionName;
        }
        catch
        {
            linkedCancellation.Cancel();
            await ObserveAsync(prepared?.ClientJarDownload, installFilesTask).ConfigureAwait(false);
            if (prepared is not null)
                await prepared.CleanupAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task AwaitTogetherOrCancelAsync(
        Task clientJarTask,
        Task installFilesTask,
        CancellationTokenSource cancellation)
    {
        var allTasks = Task.WhenAll(clientJarTask, installFilesTask);
        var firstCompleted = await Task.WhenAny(clientJarTask, installFilesTask).ConfigureAwait(false);
        if (firstCompleted.IsFaulted || firstCompleted.IsCanceled)
            cancellation.Cancel();
        await allTasks.ConfigureAwait(false);
    }

    private static async Task ObserveAsync(params Task?[] tasks)
    {
        foreach (var task in tasks)
        {
            if (task is null)
                continue;
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch { }
        }
    }
}
