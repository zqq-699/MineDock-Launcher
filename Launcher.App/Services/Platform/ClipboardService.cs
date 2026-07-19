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

using System.Threading;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace Launcher.App.Services;

public sealed class ClipboardService : IClipboardService
{
    private const int RetryCount = 3;
    private const int RetryDelayMilliseconds = 20;

    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger<ClipboardService> logger;

    public ClipboardService(
        IUiDispatcher uiDispatcher,
        ILogger<ClipboardService> logger)
    {
        this.uiDispatcher = uiDispatcher;
        this.logger = logger;
    }

    public async Task<bool> CopyTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var copied = false;
        Exception? lastException = null;
        await uiDispatcher.InvokeAsync(async () =>
        {
            for (var attempt = 0; attempt < RetryCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Clipboard.SetText flushes the OLE clipboard. On some Windows systems the
                    // text is already available even though that follow-up flush throws
                    // CLIPBRD_E_CANT_OPEN, which produces a false failure in the UI. Keep the
                    // data owned by the launcher's long-lived UI STA instead.
                    Clipboard.SetDataObject(text, copy: false);
                    copied = true;
                    return;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                    if (attempt + 1 < RetryCount)
                    {
                        await Task.Delay(
                            RetryDelayMilliseconds,
                            cancellationToken);
                    }
                }
            }
        });

        if (!copied)
            logger.LogWarning(lastException, "Failed to write text to the Windows clipboard after retries.");
        return copied;
    }

    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? text = null;
        var completed = false;
        Exception? lastException = null;
        await uiDispatcher.InvokeAsync(async () =>
        {
            for (var attempt = 0; attempt < RetryCount; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
                    completed = true;
                    return;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                    if (attempt + 1 < RetryCount)
                    {
                        await Task.Delay(
                            RetryDelayMilliseconds,
                            cancellationToken);
                    }
                }
            }
        });

        if (!completed)
        {
            logger.LogWarning(lastException, "Failed to read text from the Windows clipboard after retries.");
            return null;
        }
        return text;
    }
}
