/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Multiplayer;

public sealed partial class TerracottaAgreementDialogViewModel : ObservableObject
{
    internal const string TerracottaProjectUrl = "https://github.com/burningtnt/Terracotta";

    private readonly ITerracottaProvisioningService provisioningService;
    private readonly IExternalLinkService externalLinkService;
    private readonly IStatusService statusService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly ILogger<TerracottaAgreementDialogViewModel> logger;
    private TaskCompletionSource<bool>? pendingDecision;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private bool isDownloading;

    [ObservableProperty]
    private double downloadProgressPercent;

    [ObservableProperty]
    private string downloadStatus = Strings.Dialog_TerracottaDownloadPreparing;

    public TerracottaAgreementDialogViewModel(
        ITerracottaProvisioningService provisioningService,
        IExternalLinkService externalLinkService,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        ILogger<TerracottaAgreementDialogViewModel>? logger = null)
    {
        this.provisioningService = provisioningService;
        this.externalLinkService = externalLinkService;
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.logger = logger ?? NullLogger<TerracottaAgreementDialogViewModel>.Instance;
    }

    public Task<bool> EnsureReadyAsync()
    {
        if (provisioningService.TryGetAvailable() is not null)
            return Task.FromResult(true);

        if (pendingDecision is not null)
            return pendingDecision.Task;

        pendingDecision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DownloadProgressPercent = 0;
        DownloadStatus = Strings.Dialog_TerracottaDownloadPreparing;
        IsOpen = true;
        logger.LogInformation("Terracotta usage notice requested because the local module is unavailable.");
        return pendingDecision.Task;
    }

    [RelayCommand(CanExecute = nameof(CanRespond))]
    private void Disagree()
    {
        IsOpen = false;
        CompleteDecision(false);
        logger.LogInformation("Terracotta usage notice declined; multiplayer navigation canceled.");
    }

    [RelayCommand(CanExecute = nameof(CanRespond))]
    private async Task AgreeAsync()
    {
        IsDownloading = true;
        DownloadProgressPercent = 0;
        DownloadStatus = Strings.Dialog_TerracottaDownloading;
        try
        {
            var progress = new Progress<LauncherProgress>(value =>
            {
                if (value.Percent is { } percent)
                    DownloadProgressPercent = Math.Clamp(percent, 0, 100);
                DownloadStatus = value.Stage == "terracotta-extract"
                    ? Strings.Dialog_TerracottaExtracting
                    : Strings.Dialog_TerracottaDownloading;
            });
            await provisioningService.EnsureAvailableAsync(progress);
            DownloadProgressPercent = 100;
            DownloadStatus = Strings.Dialog_TerracottaDownloadComplete;
            IsOpen = false;
            CompleteDecision(true);
            statusService.Report(Strings.Status_TerracottaReady);
            logger.LogInformation("Terracotta usage notice accepted and module provisioning completed.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to provision the Terracotta module after notice acceptance.");
            DownloadProgressPercent = 0;
            DownloadStatus = Strings.Dialog_TerracottaDownloadFailed;
            ReportFailure(Strings.Status_TerracottaDownloadFailed);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void OpenProject()
    {
        try
        {
            if (externalLinkService.TryOpen(TerracottaProjectUrl))
                return;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to open the Terracotta project page.");
            ReportFailure(Strings.Status_OpenTerracottaProjectFailed);
            return;
        }

        logger.LogWarning("Failed to open the Terracotta project page.");
        ReportFailure(Strings.Status_OpenTerracottaProjectFailed);
    }

    private bool CanRespond() => !IsDownloading;

    partial void OnIsDownloadingChanged(bool value)
    {
        AgreeCommand.NotifyCanExecuteChanged();
        DisagreeCommand.NotifyCanExecuteChanged();
    }

    private void CompleteDecision(bool result)
    {
        var decision = pendingDecision;
        pendingDecision = null;
        decision?.TrySetResult(result);
    }

    private void ReportFailure(string message)
    {
        statusService.Report(message);
        floatingMessageService.Show(message);
    }
}
