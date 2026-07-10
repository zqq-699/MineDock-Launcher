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

using System.Collections.ObjectModel;
using System.IO;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed class LocalModsViewModel : IDisposable
{
    private const string EnabledModExtension = ".jar";
    private const string DisabledModExtension = ".jar.disabled";
    private static readonly TimeSpan IgnoredWatcherPathTtl = TimeSpan.FromSeconds(2);
    private readonly IModService modService;
    private readonly ILocalModIconEnrichmentService? iconEnrichmentService;
    private readonly IStatusService statusService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<LocalModsViewModel> logger;
    private readonly InstanceContentRefreshWatcher contentWatcher;
    private readonly object ignoredWatcherPathsLock = new();
    private readonly Dictionary<string, DateTimeOffset> ignoredWatcherPaths = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? refreshCancellationTokenSource;
    private CancellationTokenSource? iconEnrichmentCancellationTokenSource;
    private GameInstance? selectedInstance;
    private IReadOnlyList<LocalMod> currentMods = Array.Empty<LocalMod>();
    private int modRefreshVersion;

    public LocalModsViewModel(
        IModService modService,
        IStatusService statusService,
        IInstanceDirectoryMonitor instanceDirectoryMonitor,
        IUiDispatcher? uiDispatcher = null,
        ILocalModIconEnrichmentService? iconEnrichmentService = null,
        ILogger<LocalModsViewModel>? logger = null)
    {
        this.modService = modService;
        this.iconEnrichmentService = iconEnrichmentService;
        this.statusService = statusService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<LocalModsViewModel>.Instance;
        contentWatcher = new InstanceContentRefreshWatcher(
            instanceDirectoryMonitor,
            InstanceDirectoryKind.Mods,
            RefreshModsAsync,
            _ => this.uiDispatcher.Post(() => ReportStatus(Strings.Status_LoadLocalModsFailed)),
            this.logger,
            ShouldRefreshForDirectoryChange);
    }

    public event EventHandler? ModsChanged;

    public ObservableCollection<LocalMod> Mods { get; } = [];

    public IReadOnlyList<LocalMod> CurrentMods => currentMods;

    public void SetSelectedInstance(GameInstance? instance)
    {
        selectedInstance = instance;
        Interlocked.Increment(ref modRefreshVersion);
        CancelRefresh();
        CancelIconEnrichment();
        contentWatcher.SetInstance(instance);
        ClearMods();
        logger.LogInformation(
            "Selected instance changed for local mods view. InstanceId={InstanceId}",
            instance?.Id ?? "<none>");
    }

    public void SetWatcherEnabled(bool enabled)
    {
        contentWatcher.SetEnabled(enabled);
    }

    public void SuspendWatcherForInstanceRename()
    {
        contentWatcher.Suspend();
        CancelRefresh();
    }

    public void ResumeWatcherAfterInstanceRename()
    {
        contentWatcher.Resume();
    }

    public Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        SetSelectedInstance(instance);
        SetWatcherEnabled(true);
        return RefreshModsAsync();
    }

    public async Task RefreshModsAsync()
    {
        var refreshVersion = Interlocked.Increment(ref modRefreshVersion);
        var refreshCts = ReplaceRefreshCancellationTokenSource();
        var instance = selectedInstance;

        if (instance is null)
        {
            ClearMods();
            logger.LogInformation("Local mods view cleared because no instance is selected.");
            return;
        }

        IReadOnlyList<LocalMod> loadedMods;
        try
        {
            loadedMods = await modService.GetModsAsync(instance, refreshCts.Token);
            if (IsRefreshCurrent(instance, refreshVersion))
                await ApplyCachedIconSourcesAsync(instance, loadedMods, refreshCts.Token);
        }
        catch (OperationCanceledException) when (refreshCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to load local mods. InstanceId={InstanceId}",
                instance.Id);
            throw;
        }
        finally
        {
            ReleaseRefreshCancellationTokenSource(refreshCts);
        }

        if (!IsRefreshCurrent(instance, refreshVersion))
        {
            return;
        }

        uiDispatcher.Invoke(() =>
        {
            currentMods = loadedMods;
            Mods.ReplaceWith(loadedMods);
            ModsChanged?.Invoke(this, EventArgs.Empty);
        });
        logger.LogInformation(
            "Local mods view refreshed. InstanceId={InstanceId} Count={ModCount}",
            instance.Id,
            Mods.Count);
        QueueRemoteIconEnrichment(instance, loadedMods, refreshVersion);
    }

    private async Task ApplyCachedIconSourcesAsync(
        GameInstance instance,
        IReadOnlyList<LocalMod> loadedMods,
        CancellationToken cancellationToken)
    {
        if (iconEnrichmentService is null || loadedMods.Count == 0)
            return;

        IReadOnlyDictionary<string, string> cachedIcons;
        try
        {
            cachedIcons = await iconEnrichmentService
                .ResolveCachedIconSourcesAsync(loadedMods, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to resolve cached local mod icons before publishing mods. InstanceId={InstanceId}",
                instance.Id);
            return;
        }

        if (cachedIcons.Count == 0)
            return;

        var appliedCount = 0;
        foreach (var mod in loadedMods)
        {
            if (!string.IsNullOrWhiteSpace(mod.IconSource)
                || !cachedIcons.TryGetValue(mod.FullPath, out var iconSource)
                || string.IsNullOrWhiteSpace(iconSource))
            {
                continue;
            }

            mod.IconSource = iconSource;
            appliedCount++;
        }

        if (appliedCount > 0)
        {
            logger.LogInformation(
                "Applied cached local mod icons before publishing mods. InstanceId={InstanceId} Count={Count}",
                instance.Id,
                appliedCount);
        }
    }

    public async Task ToggleModAsync(LocalMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        var enabled = !mod.IsEnabled;
        var sourcePath = mod.FullPath;
        var targetPath = GetPathForEnabledState(sourcePath, enabled);
        IgnoreWatcherPaths(sourcePath, targetPath);

        try
        {
            await modService.SetEnabledAsync(mod, enabled);
        }
        catch
        {
            RemoveIgnoredWatcherPaths(sourcePath, targetPath);
            throw;
        }

        ApplyEnabledStateLocally(mod, targetPath, enabled, raiseChanged: true);
    }

    public async Task DeleteModAsync(LocalMod mod)
    {
        await modService.DeleteAsync(mod);
        await RefreshModsAsync();
    }

    public async Task<int> SetModsEnabledAsync(IEnumerable<LocalMod> mods, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(mods);

        var failedCount = 0;
        var appliedUpdates = new List<(LocalMod Mod, string TargetPath)>();
        foreach (var mod in mods
                     .Where(mod => mod.IsEnabled != enabled)
                     .DistinctBy(mod => mod.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            var sourcePath = mod.FullPath;
            var targetPath = GetPathForEnabledState(sourcePath, enabled);
            IgnoreWatcherPaths(sourcePath, targetPath);
            try
            {
                await modService.SetEnabledAsync(mod, enabled);
                appliedUpdates.Add((mod, targetPath));
            }
            catch (Exception exception)
            {
                RemoveIgnoredWatcherPaths(sourcePath, targetPath);
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to change local mod enabled state. Path={Path} Enabled={Enabled}",
                    mod.FullPath,
                    enabled);
            }
        }

        if (appliedUpdates.Count > 0)
        {
            uiDispatcher.Invoke(() =>
            {
                foreach (var (mod, targetPath) in appliedUpdates)
                    ApplyEnabledStateLocallyCore(mod, targetPath, enabled);

                ModsChanged?.Invoke(this, EventArgs.Empty);
            });
        }

        return failedCount;
    }

    public async Task<int> DeleteModsAsync(IEnumerable<LocalMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);

        var failedCount = 0;
        foreach (var mod in mods.DistinctBy(mod => mod.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await modService.DeleteAsync(mod);
            }
            catch (Exception exception)
            {
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to delete local mod. Path={Path}",
                    mod.FullPath);
            }
        }

        await RefreshModsAsync();
        return failedCount;
    }

    public async Task<bool> ImportModFromPathAsync(string path, bool overwriteExisting = false, bool reportStatus = true)
    {
        if (selectedInstance is null || string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            await modService.ImportAsync(selectedInstance, path, overwriteExisting);
        }
        catch (ModFileImportNotFoundException)
        {
            if (reportStatus)
                ReportStatus(Strings.Status_LocalModImportFileNotFound);
            return false;
        }

        await RefreshModsAsync();
        if (reportStatus)
            ReportStatus(Strings.Status_LocalModImported);
        return true;
    }

    public void Dispose()
    {
        contentWatcher.Dispose();
        CancelRefresh();
        CancelIconEnrichment();
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }

    private void ClearMods()
    {
        uiDispatcher.Invoke(() =>
        {
            currentMods = Array.Empty<LocalMod>();
            Mods.Clear();
            ModsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void CancelRefresh()
    {
        refreshCancellationTokenSource?.Cancel();
        refreshCancellationTokenSource?.Dispose();
        refreshCancellationTokenSource = null;
    }

    private CancellationTokenSource ReplaceRefreshCancellationTokenSource()
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref refreshCancellationTokenSource, next);
        previous?.Cancel();
        previous?.Dispose();
        CancelIconEnrichment();
        return next;
    }

    private void ReleaseRefreshCancellationTokenSource(CancellationTokenSource refreshCts)
    {
        var current = Interlocked.CompareExchange(ref refreshCancellationTokenSource, null, refreshCts);
        if (ReferenceEquals(current, refreshCts))
            refreshCts.Dispose();
    }

    private void QueueRemoteIconEnrichment(GameInstance instance, IReadOnlyList<LocalMod> loadedMods, int refreshVersion)
    {
        if (iconEnrichmentService is null)
            return;

        var missingIconMods = loadedMods
            .Where(mod => string.IsNullOrWhiteSpace(mod.IconSource))
            .ToArray();
        if (missingIconMods.Length == 0)
            return;

        var enrichmentCts = ReplaceIconEnrichmentCancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<IReadOnlyDictionary<string, string>>(resolvedIcons =>
                    ApplyResolvedIcons(instance, resolvedIcons, enrichmentCts, refreshVersion));
                var resolvedIcons = await iconEnrichmentService
                    .ResolveMissingIconSourcesAsync(missingIconMods, enrichmentCts.Token, progress)
                    .ConfigureAwait(false);
                ApplyResolvedIcons(instance, resolvedIcons, enrichmentCts, refreshVersion);
            }
            catch (OperationCanceledException) when (enrichmentCts.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to enrich local mod icons. InstanceId={InstanceId}",
                    instance.Id);
            }
            finally
            {
                ReleaseIconEnrichmentCancellationTokenSource(enrichmentCts);
            }
        });
    }

    private void ApplyResolvedIcons(
        GameInstance instance,
        IReadOnlyDictionary<string, string> resolvedIcons,
        CancellationTokenSource enrichmentCts,
        int refreshVersion)
    {
        if (resolvedIcons.Count == 0
            || enrichmentCts.IsCancellationRequested
            || refreshVersion != modRefreshVersion
            || !string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal))
        {
            return;
        }

        uiDispatcher.Post(() =>
        {
            if (enrichmentCts.IsCancellationRequested
                || refreshVersion != modRefreshVersion
                || !string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal))
            {
                return;
            }

            var updated = false;
            foreach (var mod in currentMods)
            {
                if (!string.IsNullOrWhiteSpace(mod.IconSource)
                    || !resolvedIcons.TryGetValue(mod.FullPath, out var iconSource)
                    || string.IsNullOrWhiteSpace(iconSource))
                {
                    continue;
                }

                mod.IconSource = iconSource;
                updated = true;
            }

            if (updated)
                ModsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private bool IsRefreshCurrent(GameInstance instance, int refreshVersion)
    {
        return refreshVersion == modRefreshVersion
            && string.Equals(instance.Id, selectedInstance?.Id, StringComparison.Ordinal);
    }

    private void CancelIconEnrichment()
    {
        iconEnrichmentCancellationTokenSource?.Cancel();
        iconEnrichmentCancellationTokenSource?.Dispose();
        iconEnrichmentCancellationTokenSource = null;
    }

    private CancellationTokenSource ReplaceIconEnrichmentCancellationTokenSource()
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref iconEnrichmentCancellationTokenSource, next);
        previous?.Cancel();
        previous?.Dispose();
        return next;
    }

    private void ReleaseIconEnrichmentCancellationTokenSource(CancellationTokenSource enrichmentCts)
    {
        var current = Interlocked.CompareExchange(ref iconEnrichmentCancellationTokenSource, null, enrichmentCts);
        if (ReferenceEquals(current, enrichmentCts))
            enrichmentCts.Dispose();
    }

    private static bool IsTrackedModPath(string? fullPath)
    {
        return !string.IsNullOrWhiteSpace(fullPath)
            && (fullPath.EndsWith(EnabledModExtension, StringComparison.OrdinalIgnoreCase)
                || fullPath.EndsWith(DisabledModExtension, StringComparison.OrdinalIgnoreCase));
    }

    private bool ShouldRefreshForDirectoryChange(InstanceDirectoryChangedEventArgs change)
    {
        if (!IsTrackedModPath(change.FullPath) && !IsTrackedModPath(change.OldFullPath))
            return false;

        return !ShouldIgnoreWatcherPath(change.FullPath)
            && !ShouldIgnoreWatcherPath(change.OldFullPath);
    }

    private void ApplyEnabledStateLocally(LocalMod mod, string targetPath, bool enabled, bool raiseChanged)
    {
        uiDispatcher.Invoke(() =>
        {
            ApplyEnabledStateLocallyCore(mod, targetPath, enabled);
            if (raiseChanged)
                ModsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private static void ApplyEnabledStateLocallyCore(LocalMod mod, string targetPath, bool enabled)
    {
        mod.FullPath = targetPath;
        mod.FileName = Path.GetFileName(targetPath);
        mod.IsEnabled = enabled;
    }

    private void IgnoreWatcherPaths(params string?[] paths)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(IgnoredWatcherPathTtl);
        lock (ignoredWatcherPathsLock)
        {
            PruneIgnoredWatcherPaths(DateTimeOffset.UtcNow);
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                ignoredWatcherPaths[path] = expiresAt;
            }
        }
    }

    private bool ShouldIgnoreWatcherPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var now = DateTimeOffset.UtcNow;
        lock (ignoredWatcherPathsLock)
        {
            PruneIgnoredWatcherPaths(now);
            return ignoredWatcherPaths.Remove(path);
        }
    }

    private void RemoveIgnoredWatcherPaths(params string?[] paths)
    {
        lock (ignoredWatcherPathsLock)
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                ignoredWatcherPaths.Remove(path);
            }
        }
    }

    private void PruneIgnoredWatcherPaths(DateTimeOffset now)
    {
        foreach (var path in ignoredWatcherPaths
                     .Where(pair => pair.Value <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            ignoredWatcherPaths.Remove(path);
        }
    }

    private static string GetPathForEnabledState(string path, bool enabled)
    {
        return enabled
            ? path.EndsWith(DisabledModExtension, StringComparison.OrdinalIgnoreCase)
                ? path[..^".disabled".Length]
                : path
            : path.EndsWith(DisabledModExtension, StringComparison.OrdinalIgnoreCase)
                ? path
                : path + ".disabled";
    }
}
