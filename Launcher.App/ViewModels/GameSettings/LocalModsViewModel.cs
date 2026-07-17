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

/// <summary>
/// 维护所选实例的 Mod 快照、目录监听和图标渐进补全，并保证异步结果只作用于当前实例。
/// </summary>
public sealed class LocalModsViewModel : IDisposable
{
    // 启用状态通过文件后缀表达，因此重命名既是业务操作也会产生目录 watcher 回声。
    private const string EnabledModExtension = ".jar";
    private const string DisabledModExtension = ".jar.disabled";
    private static readonly TimeSpan IgnoredWatcherPathTtl = TimeSpan.FromSeconds(2);
    // 文件操作和图标解析都在服务层完成；本类只维护当前实例快照及通知顺序。
    private readonly IModService modService;
    private readonly ILocalModIconEnrichmentService? iconEnrichmentService;
    private readonly LocalResourceCategoryEnrichmentCoordinator<LocalMod> categoryEnrichmentCoordinator;
    private readonly IStatusService statusService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<LocalModsViewModel> logger;
    private readonly InstanceContentRefreshWatcher contentWatcher;
    // 忽略表可能由 UI 操作和 watcher 线程同时访问，必须使用独立锁并设置短 TTL。
    private readonly object ignoredWatcherPathsLock = new();
    private readonly Dictionary<string, DateTimeOffset> ignoredWatcherPaths = new(StringComparer.OrdinalIgnoreCase);
    // 列表扫描和远程图标补全独立取消：新扫描立即停止旧图标，但缓存图标仍可先发布。
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
        ILogger<LocalModsViewModel>? logger = null,
        ILocalResourceCategoryEnrichmentService? categoryEnrichmentService = null)
    {
        this.modService = modService;
        this.iconEnrichmentService = iconEnrichmentService;
        this.statusService = statusService;
        this.uiDispatcher = uiDispatcher ?? ImmediateUiDispatcher.Instance;
        this.logger = logger ?? NullLogger<LocalModsViewModel>.Instance;
        categoryEnrichmentCoordinator = new LocalResourceCategoryEnrichmentCoordinator<LocalMod>(
            categoryEnrichmentService,
            ResourceProjectKind.Mod,
            mod => mod.FullPath,
            mod => mod.Categories,
            static (mod, categories) => mod.Categories = categories,
            () => currentMods,
            () => ModsChanged?.Invoke(this, EventArgs.Empty),
            this.uiDispatcher,
            this.logger);
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

    /// <summary>
    /// 切换实例并重置 Mod 快照、监听目标以及图标补全任务。
    /// </summary>
    public void SetSelectedInstance(GameInstance? instance)
    {
        // 切换实例时立即使加载和图标补全失效，避免旧实例结果在稍后覆盖新实例列表。
        selectedInstance = instance;
        Interlocked.Increment(ref modRefreshVersion);
        CancelRefresh();
        CancelIconEnrichment();
        categoryEnrichmentCoordinator.Cancel();
        contentWatcher.SetInstance(instance);
        ClearMods();
        logger.LogInformation(
            "Selected instance changed for local mods view. InstanceId={InstanceId}",
            instance?.Id ?? "<none>");
    }

    public void SetWatcherEnabled(bool enabled)
    {
        contentWatcher.SetEnabled(enabled);
        if (!enabled)
        {
            Interlocked.Increment(ref modRefreshVersion);
            CancelRefresh();
            CancelIconEnrichment();
            categoryEnrichmentCoordinator.Cancel();
        }
    }

    public void SuspendWatcherForInstanceRename()
    {
        contentWatcher.Suspend();
        CancelRefresh();
    }

    public void ResumeWatcherAfterInstanceRename(bool restart = true)
    {
        contentWatcher.Resume(restart);
    }

    /// <summary>
    /// 重新扫描当前实例 Mod；缓存图标随首屏结果发布，远程图标随后渐进补全。
    /// </summary>
    public async Task<bool> RefreshModsAsync()
    {
        // 版本号负责兜底拒绝不响应取消的旧请求，CTS 负责尽快停止仍在执行的 I/O。
        var refreshVersion = Interlocked.Increment(ref modRefreshVersion);
        var refreshCts = ReplaceRefreshCancellationTokenSource();
        // 捕获实例引用和版本号作为本次请求身份，后续不直接相信可变 selectedInstance。
        var instance = selectedInstance;

        if (instance is null)
        {
            ClearMods();
            logger.LogInformation("Local mods view cleared because no instance is selected.");
            return true;
        }

        IReadOnlyList<LocalMod> loadedMods;
        try
        {
            // 阶段一读取本地 Mod；阶段二只查询缓存，不让远端网络阻塞列表首屏。
            loadedMods = await modService.GetModsAsync(instance, refreshCts.Token);
            if (IsRefreshCurrent(instance, refreshVersion))
                await ApplyCachedIconSourcesAsync(instance, loadedMods, refreshCts.Token);
        }
        catch (OperationCanceledException) when (refreshCts.IsCancellationRequested)
        {
            return false;
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

        // 服务可能忽略取消令牌，发布前仍需验证实例和刷新代次。
        if (!IsRefreshCurrent(instance, refreshVersion))
        {
            return false;
        }

        // 集合替换和 ModsChanged 在同一个 UI 临界区完成，观察者不会看到两份快照不一致。
        var published = false;
        uiDispatcher.Invoke(() =>
        {
            if (!IsRefreshCurrent(instance, refreshVersion))
                return;

            currentMods = loadedMods;
            Mods.ReplaceWith(loadedMods);
            ModsChanged?.Invoke(this, EventArgs.Empty);
            published = true;
        });
        if (!published)
            return false;
        logger.LogInformation(
            "Local mods view refreshed. InstanceId={InstanceId} Count={ModCount}",
            instance.Id,
            Mods.Count);
        // 远程补全是非阻断增强，失败不会撤回已经可用的本地列表。
        QueueRemoteIconEnrichment(instance, loadedMods, refreshVersion);
        categoryEnrichmentCoordinator.Queue(loadedMods);
        return true;
    }

    /// <summary>
    /// 在列表首次发布前应用已有图标缓存，避免可复用图标再次闪烁加载。
    /// </summary>
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
            // 缓存查询失败只降低首屏图标命中率，不能让 Mod 列表加载失败。
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
        // 内嵌图标优先级更高，只给尚无 IconSource 的项目应用远程缓存。
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

    /// <summary>
    /// 通过切换 .disabled 后缀改变单个 Mod 状态，并同步本地快照。
    /// </summary>
    public async Task ToggleModAsync(LocalMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        var enabled = !mod.IsEnabled;
        var sourcePath = mod.FullPath;
        var targetPath = GetPathForEnabledState(sourcePath, enabled);
        // 本次重命名会被目录监听器再次观察到；短暂忽略源/目标路径可避免重复刷新和界面闪烁。
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

    /// <summary>
    /// 批量切换 Mod 状态；允许部分失败并返回失败数量，成功项一次性发布到界面。
    /// </summary>
    public async Task<int> SetModsEnabledAsync(IEnumerable<LocalMod> mods, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(mods);

        // 先收集成功项，循环结束后一次性更新模型，避免 watcher/界面在中间状态反复刷新。
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
                // 单项失败移除忽略路径，否则真实失败后的 watcher 事件会被错误吞掉。
                RemoveIgnoredWatcherPaths(sourcePath, targetPath);
                failedCount++;
                logger.LogWarning(
                    exception,
                    "Failed to change local mod enabled state. Path={Path} Enabled={Enabled}",
                    mod.FullPath,
                    enabled);
            }
        }

        // 成功项沿用服务实际目标路径，保证选择和后续删除都指向新文件名。
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
        categoryEnrichmentCoordinator.Dispose();
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

    /// <summary>
    /// 在后台解析尚无图标的 Mod，并把结果绑定到发起刷新时的实例和代次。
    /// </summary>
    private void QueueRemoteIconEnrichment(GameInstance instance, IReadOnlyList<LocalMod> loadedMods, int refreshVersion)
    {
        if (iconEnrichmentService is null)
            return;

        var missingIconMods = loadedMods
            .Where(mod => string.IsNullOrWhiteSpace(mod.IconSource))
            .ToArray();
        if (missingIconMods.Length == 0)
            return;

        // 新补全任务接管唯一 CTS；旧任务即使结束也不能释放新任务的取消源。
        var enrichmentCts = ReplaceIconEnrichmentCancellationTokenSource();
        // 后台任务只执行网络/缓存工作，任何模型写入都通过 ApplyResolvedIcons 回到 UI Dispatcher。
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
                // 图标属于可选增强，记录警告后保留现有列表和已解析的缓存图标。
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

    /// <summary>
    /// 将远程图标结果合并到当前快照；过期、已取消或已切换实例的结果会被丢弃。
    /// </summary>
    private void ApplyResolvedIcons(
        GameInstance instance,
        IReadOnlyDictionary<string, string> resolvedIcons,
        CancellationTokenSource enrichmentCts,
        int refreshVersion)
    {
        // 第一次检查避免无效任务排队到 UI 线程。
        if (resolvedIcons.Count == 0
            || enrichmentCts.IsCancellationRequested
            || refreshVersion != modRefreshVersion
            || !IsSameInstancePath(instance, selectedInstance))
        {
            return;
        }

        uiDispatcher.Post(() =>
        {
            // 排队等待 UI 线程期间所选实例仍可能变化，必须在真正写入模型前再次校验。
            if (enrichmentCts.IsCancellationRequested
                || refreshVersion != modRefreshVersion
                || !IsSameInstancePath(instance, selectedInstance))
            {
                return;
            }

            // 第二次检查通过后只填空值，不覆盖导入扫描期间新发现的内嵌图标。
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
            && IsSameInstancePath(instance, selectedInstance);
    }

    private static bool IsSameInstancePath(GameInstance left, GameInstance? right)
    {
        return right is not null
            && string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && string.Equals(left.InstanceDirectory, right.InstanceDirectory, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// 过滤与 Mod 无关的目录事件，并忽略由本 ViewModel 主动重命名产生的回声事件。
    /// </summary>
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
        // 同一文件重命名通常产生旧路径和新路径两个事件，两者都在短窗口内登记。
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
            // 忽略项只消费一次；TTL 仅用于处理监听器未产生对应事件时的残留记录。
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
