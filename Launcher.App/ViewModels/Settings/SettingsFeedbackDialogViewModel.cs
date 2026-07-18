/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class SettingsFeedbackDialogViewModel : ObservableObject
{
    private readonly IStatusService statusService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly IExternalLinkService externalLinkService;
    private readonly ILogger<SettingsFeedbackDialogViewModel> logger;

    public SettingsFeedbackDialogViewModel(
        IStatusService statusService,
        IFloatingMessageService floatingMessageService,
        IExternalLinkService externalLinkService,
        ILogger<SettingsFeedbackDialogViewModel>? logger = null)
    {
        this.statusService = statusService;
        this.floatingMessageService = floatingMessageService;
        this.externalLinkService = externalLinkService;
        this.logger = logger ?? NullLogger<SettingsFeedbackDialogViewModel>.Instance;
    }

    [ObservableProperty]
    private bool isOpen;

    public void Open()
    {
        IsOpen = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
    }

    [RelayCommand]
    private void OpenFeatureSuggestions()
    {
        OpenExternalLink(LauncherProjectLinks.GitHubFeatureSuggestionsUrl, "feature-suggestions");
    }

    [RelayCommand]
    private void OpenBugReports()
    {
        OpenExternalLink(LauncherProjectLinks.GitHubIssuesUrl, "bug-reports");
    }

    private void OpenExternalLink(string url, string target)
    {
        try
        {
            logger.LogDebug("Opening feedback link. Target={Target}", target);
            if (externalLinkService.TryOpen(url))
            {
                logger.LogInformation("Feedback link opened.");
                logger.LogDebug("Opened feedback link target. Target={Target}", target);
                return;
            }

            logger.LogWarning("Unable to open feedback link. Target={Target}", target);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to open feedback link. Target={Target}", target);
        }

        statusService.Report(Strings.Status_OpenFeedbackPageFailed);
        floatingMessageService.Show(Strings.Status_OpenFeedbackPageFailed);
    }
}
