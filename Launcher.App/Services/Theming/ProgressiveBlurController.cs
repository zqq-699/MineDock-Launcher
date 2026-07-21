/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Effects;
using Microsoft.Extensions.Logging;

namespace Launcher.App.Services;

internal sealed class ProgressiveBlurController : IDisposable
{
    private readonly IUiDispatcher uiDispatcher;
    private readonly IProgressiveBlurSupport support;
    private readonly ILogger logger;
    private ProgressiveBlurCapabilitySnapshot? lastLoggedCapability;
    private bool? lastLoggedState;
    private bool isInitialized;
    private bool isDisposed;

    public ProgressiveBlurController(
        IUiDispatcher uiDispatcher,
        IProgressiveBlurSupport support,
        ILogger logger)
    {
        this.uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        this.support = support ?? throw new ArgumentNullException(nameof(support));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        support.AvailabilityChanged += Support_AvailabilityChanged;
    }

    public void Initialize()
    {
        if (isDisposed || isInitialized)
            return;

        isInitialized = true;
        ApplyCurrent();
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        support.AvailabilityChanged -= Support_AvailabilityChanged;
        support.Dispose();
        isDisposed = true;
    }

    private void Support_AvailabilityChanged(object? sender, EventArgs e)
    {
        if (isDisposed)
            return;

        uiDispatcher.Invoke(ApplyCurrent);
    }

    private void ApplyCurrent()
    {
        var application = global::System.Windows.Application.Current;
        if (application is null)
            return;

        var capability = support.Current;
        application.Resources[ProgressiveBlurResourceKeys.IsEnabled] = capability.IsAvailable;
        LogCapability(capability);
        if (lastLoggedState == capability.IsAvailable)
            return;

        logger.LogDebug(
            "Progressive blur availability applied. ProgressiveBlurActive={ProgressiveBlurActive}",
            capability.IsAvailable);
        lastLoggedState = capability.IsAvailable;
    }

    private void LogCapability(ProgressiveBlurCapabilitySnapshot capability)
    {
        if (lastLoggedCapability == capability)
            return;

        if (capability.UnavailableReason is ProgressiveBlurUnavailableReason.ShaderLoadFailed
            && capability.InitializationException is not null)
        {
            logger.LogWarning(
                capability.InitializationException,
                "Progressive blur shader initialization failed; opacity fade fallback will be used. RenderTier={RenderTier} ShaderModel=3.0 HardwareOnly=True",
                capability.RenderingTier);
        }
        else if (capability.UnavailableReason is ProgressiveBlurUnavailableReason.ShaderRejected)
        {
            logger.LogWarning(
                "Progressive blur shader was rejected by WPF; opacity fade fallback will be used. RenderTier={RenderTier} ShaderModel=3.0 HardwareOnly=True",
                capability.RenderingTier);
        }
        else
        {
            logger.LogDebug(
                "Progressive blur capability evaluated. Supported={Supported} RenderTier={RenderTier} PixelShader30Supported={PixelShader30Supported} ShaderModel=3.0 Reason={Reason} HardwareOnly=True",
                capability.IsAvailable,
                capability.RenderingTier,
                capability.IsPixelShader30Supported,
                capability.UnavailableReason);
        }

        lastLoggedCapability = capability;
    }
}
