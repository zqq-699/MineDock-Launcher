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

public sealed partial class EasyTierAgreementDialogViewModel : ObservableObject
{
    internal const string EasyTierLicenseUrl = "https://easytier.cn/guide/license.html";
    internal const string EasyTierPrivacyUrl = "https://easytier.cn/guide/privacy.html";

    private readonly IEasyTierProvisioningService provisioningService;
    private readonly IExternalLinkService externalLinkService;
    private readonly IStatusService statusService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly ILogger<EasyTierAgreementDialogViewModel> logger;
    private TaskCompletionSource<bool>? pendingDecision;

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private bool isDownloading;

    [ObservableProperty]
    private double downloadProgressPercent;

    [ObservableProperty]
    private string downloadStatus = Strings.Dialog_EasyTierDownloadPreparing;

    public EasyTierAgreementDialogViewModel(
        IEasyTierProvisioningService provisioningService,
        IExternalLinkService externalLinkService,
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        ILogger<EasyTierAgreementDialogViewModel>? logger = null)
    {
        this.provisioningService = provisioningService;
        this.externalLinkService = externalLinkService;
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.logger = logger ?? NullLogger<EasyTierAgreementDialogViewModel>.Instance;
    }

    public Task<bool> EnsureReadyAsync()
    {
        if (provisioningService.TryGetAvailable() is not null)
            return Task.FromResult(true);

        if (pendingDecision is not null)
            return pendingDecision.Task;

        pendingDecision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DownloadProgressPercent = 0;
        DownloadStatus = Strings.Dialog_EasyTierDownloadPreparing;
        IsOpen = true;
        logger.LogInformation("EasyTier agreement requested because the local module is unavailable.");
        return pendingDecision.Task;
    }

    [RelayCommand(CanExecute = nameof(CanRespond))]
    private void Disagree()
    {
        IsOpen = false;
        CompleteDecision(false);
        logger.LogInformation("EasyTier agreement declined; multiplayer navigation canceled.");
    }

    [RelayCommand(CanExecute = nameof(CanRespond))]
    private async Task AgreeAsync()
    {
        IsDownloading = true;
        DownloadProgressPercent = 0;
        DownloadStatus = Strings.Dialog_EasyTierDownloading;
        try
        {
            var progress = new Progress<LauncherProgress>(value =>
            {
                if (value.Percent is { } percent)
                    DownloadProgressPercent = Math.Clamp(percent, 0, 100);
                DownloadStatus = value.Stage == "easytier-extract"
                    ? Strings.Dialog_EasyTierExtracting
                    : Strings.Dialog_EasyTierDownloading;
            });
            await provisioningService.EnsureAvailableAsync(progress);
            DownloadProgressPercent = 100;
            DownloadStatus = Strings.Dialog_EasyTierDownloadComplete;
            IsOpen = false;
            CompleteDecision(true);
            statusService.Report(Strings.Status_EasyTierReady);
            logger.LogInformation("EasyTier agreement accepted and module provisioning completed.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to provision the EasyTier module after agreement acceptance.");
            DownloadProgressPercent = 0;
            DownloadStatus = Strings.Dialog_EasyTierDownloadFailed;
            ReportFailure(Strings.Status_EasyTierDownloadFailed);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void OpenAgreement() => OpenLegalDocument(EasyTierLicenseUrl, "license");

    [RelayCommand]
    private void OpenPrivacyPolicy() => OpenLegalDocument(EasyTierPrivacyUrl, "privacy-policy");

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

    private void OpenLegalDocument(string url, string documentName)
    {
        try
        {
            if (externalLinkService.TryOpen(url))
                return;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to open the EasyTier legal document. Document={Document}", documentName);
            ReportFailure(Strings.Status_OpenEasyTierLegalDocumentFailed);
            return;
        }

        logger.LogWarning("Failed to open the EasyTier legal document. Document={Document}", documentName);
        ReportFailure(Strings.Status_OpenEasyTierLegalDocumentFailed);
    }

    private void ReportFailure(string message)
    {
        statusService.Report(message);
        floatingMessageService.Show(message);
    }
}
