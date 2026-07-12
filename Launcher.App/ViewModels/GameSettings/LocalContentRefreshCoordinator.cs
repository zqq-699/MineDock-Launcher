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

using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.GameSettings;

/// <summary>
/// 为存档、资源包和光影包页面统一处理“最后一次刷新生效”、目录监听和 UI 线程发布。
/// </summary>
internal sealed class LocalContentRefreshCoordinator<TContent> : IDisposable
{
    private readonly Func<GameInstance, CancellationToken, Task<IReadOnlyList<TContent>>> loadAsync;
    private readonly InstanceDirectoryKind directoryKind;
    private readonly Action<IReadOnlyList<TContent>> apply;
    private readonly Action clear;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger logger;
    private readonly InstanceContentRefreshWatcher watcher;
    private CancellationTokenSource? refreshCancellation;
    private int refreshGeneration;

    public LocalContentRefreshCoordinator(
        IInstanceDirectoryMonitor monitor,
        InstanceDirectoryKind directoryKind,
        Func<GameInstance, CancellationToken, Task<IReadOnlyList<TContent>>> loadAsync,
        Action<IReadOnlyList<TContent>> apply,
        Action clear,
        Action<Exception> reportWatcherFailure,
        IUiDispatcher uiDispatcher,
        ILogger logger,
        Func<InstanceDirectoryChangedEventArgs, bool>? shouldRefresh = null)
    {
        this.loadAsync = loadAsync;
        this.directoryKind = directoryKind;
        this.apply = apply;
        this.clear = clear;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger;
        watcher = new InstanceContentRefreshWatcher(
            monitor,
            directoryKind,
            RefreshAsync,
            reportWatcherFailure,
            logger,
            shouldRefresh);
    }

    public GameInstance? SelectedInstance { get; private set; }

    public IReadOnlyList<TContent> CurrentItems { get; private set; } = [];

    /// <summary>
    /// 切换内容所属实例，并使旧实例所有在途刷新立即失效。
    /// </summary>
    public void SetInstance(GameInstance? instance)
    {
        // 先使在途结果失效，再切换监听和清空界面，保证旧实例的数据不会回写到新实例。
        SelectedInstance = instance;
        Interlocked.Increment(ref refreshGeneration);
        CancelRefresh();
        watcher.SetInstance(instance);
        CurrentItems = [];
        uiDispatcher.Invoke(clear);
    }

    public void SetWatcherEnabled(bool enabled)
    {
        watcher.SetEnabled(enabled);
        if (!enabled)
            CancelRefresh();
    }

    public void SuspendForRename()
    {
        watcher.Suspend();
        CancelRefresh();
    }

    public void ResumeAfterRename() => watcher.Resume();

    /// <summary>
    /// 加载当前实例内容，并且仅在实例和刷新代次仍匹配时发布结果。
    /// </summary>
    public async Task<bool> RefreshAsync()
    {
        // generation 与 CTS 同时使用：CTS 尽快停止旧工作，generation 则在依赖不响应取消时阻止陈旧结果发布。
        var generation = Interlocked.Increment(ref refreshGeneration);
        var replacement = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref refreshCancellation, replacement);
        previous?.Cancel();
        previous?.Dispose();
        var instance = SelectedInstance;

        if (instance is null)
        {
            CurrentItems = [];
            uiDispatcher.Invoke(clear);
            Release(replacement);
            return true;
        }

        try
        {
            var items = await loadAsync(instance, replacement.Token).ConfigureAwait(false);
            if (generation != refreshGeneration
                || replacement.IsCancellationRequested
                || !string.Equals(instance.Id, SelectedInstance?.Id, StringComparison.Ordinal))
            {
                return false;
            }

            var published = false;
            uiDispatcher.Invoke(() =>
            {
                // 调度到 UI 线程期间可能又发起刷新，因此发布前需要再次验证代次。
                if (generation != refreshGeneration || replacement.IsCancellationRequested)
                    return;
                CurrentItems = items;
                apply(items);
                published = true;
            });
            return published;
        }
        catch (OperationCanceledException) when (replacement.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to refresh local instance content. InstanceId={InstanceId} DirectoryKind={DirectoryKind}", instance.Id, directoryKind);
            throw;
        }
        finally
        {
            Release(replacement);
        }
    }

    public void Dispose()
    {
        watcher.Dispose();
        CancelRefresh();
    }

    private void CancelRefresh()
    {
        var cancellation = Interlocked.Exchange(ref refreshCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void Release(CancellationTokenSource cancellation)
    {
        // 旧请求结束时不能清空新请求刚写入的 CTS。
        if (ReferenceEquals(Interlocked.CompareExchange(ref refreshCancellation, null, cancellation), cancellation))
            cancellation.Dispose();
    }
}
