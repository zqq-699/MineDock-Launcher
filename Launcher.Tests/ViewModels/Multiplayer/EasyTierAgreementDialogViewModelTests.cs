/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Multiplayer;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Multiplayer;

public sealed class EasyTierAgreementDialogViewModelTests
{
    [Fact]
    public async Task InstalledModuleAllowsEntryWithoutOpeningDialog()
    {
        var context = Create(isAvailable: true);

        var result = await context.ViewModel.EnsureReadyAsync();

        Assert.True(result);
        Assert.False(context.ViewModel.IsOpen);
        Assert.Equal(0, context.Provisioning.EnsureCount);
    }

    [Fact]
    public async Task MissingModuleOpensDialogAndDisagreeCancelsEntry()
    {
        var context = Create();
        var decision = context.ViewModel.EnsureReadyAsync();

        context.ViewModel.DisagreeCommand.Execute(null);

        Assert.False(await decision);
        Assert.False(context.ViewModel.IsOpen);
        Assert.Equal(0, context.Provisioning.EnsureCount);
    }

    [Fact]
    public async Task AgreeDownloadsModuleThenAllowsEntry()
    {
        var context = Create();
        var decision = context.ViewModel.EnsureReadyAsync();

        await context.ViewModel.AgreeCommand.ExecuteAsync(null);

        Assert.True(await decision);
        Assert.False(context.ViewModel.IsOpen);
        Assert.False(context.ViewModel.IsDownloading);
        Assert.Equal(1, context.Provisioning.EnsureCount);
        Assert.Equal(Strings.Status_EasyTierReady, context.Messages.StatusMessage);
    }

    [Fact]
    public async Task DownloadFailureKeepsDialogOpenAndEntryBlockedForRetry()
    {
        var context = Create();
        context.Provisioning.EnsureException = new IOException("Test failure.");
        var decision = context.ViewModel.EnsureReadyAsync();

        await context.ViewModel.AgreeCommand.ExecuteAsync(null);

        Assert.False(decision.IsCompleted);
        Assert.True(context.ViewModel.IsOpen);
        Assert.False(context.ViewModel.IsDownloading);
        Assert.Equal(Strings.Dialog_EasyTierDownloadFailed, context.ViewModel.DownloadStatus);
        Assert.Equal(Strings.Status_EasyTierDownloadFailed, context.Messages.StatusMessage);
        Assert.Equal(Strings.Status_EasyTierDownloadFailed, context.Messages.FloatingMessage);
    }

    [Fact]
    public void LegalLinksUseOfficialEasyTierPages()
    {
        var context = Create();

        context.ViewModel.OpenAgreementCommand.Execute(null);
        var agreementUrl = context.ExternalLinks.LastUrl;
        context.ViewModel.OpenPrivacyPolicyCommand.Execute(null);

        Assert.Equal(EasyTierAgreementDialogViewModel.EasyTierLicenseUrl, agreementUrl);
        Assert.Equal(EasyTierAgreementDialogViewModel.EasyTierPrivacyUrl, context.ExternalLinks.LastUrl);
    }

    private static TestContext Create(bool isAvailable = false)
    {
        var provisioning = new RecordingProvisioningService { IsAvailable = isAvailable };
        var externalLinks = new RecordingExternalLinkService();
        var messages = new RecordingMessageService();
        var viewModel = new EasyTierAgreementDialogViewModel(
            provisioning,
            externalLinks,
            messages,
            messages);
        return new TestContext(viewModel, provisioning, externalLinks, messages);
    }

    private sealed record TestContext(
        EasyTierAgreementDialogViewModel ViewModel,
        RecordingProvisioningService Provisioning,
        RecordingExternalLinkService ExternalLinks,
        RecordingMessageService Messages);

    private sealed class RecordingProvisioningService : IEasyTierProvisioningService
    {
        public bool IsAvailable { get; set; }
        public int EnsureCount { get; private set; }
        public Exception? EnsureException { get; set; }

        public EasyTierModule? TryGetAvailable() => IsAvailable ? CreateModule() : null;

        public Task<EasyTierModule> EnsureAvailableAsync(
            IProgress<LauncherProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            EnsureCount++;
            if (EnsureException is not null)
                return Task.FromException<EasyTierModule>(EnsureException);
            IsAvailable = true;
            progress?.Report(new LauncherProgress("easytier-ready", "Ready", 100));
            return Task.FromResult(CreateModule());
        }

        private static EasyTierModule CreateModule() => new(
            "test",
            "C:\\test",
            "C:\\test\\easytier-core.exe",
            "C:\\test\\easytier-cli.exe",
            "C:\\test\\Packet.dll");
    }

    private sealed class RecordingExternalLinkService : IExternalLinkService
    {
        public string? LastUrl { get; private set; }

        public bool TryOpen(string url)
        {
            LastUrl = url;
            return true;
        }
    }

    private sealed class RecordingMessageService : IStatusService, IFloatingMessageService
    {
        public event Action<string>? MessageReported;
        public event Action<string>? MessageRequested;

        public string? StatusMessage { get; private set; }
        public string? FloatingMessage { get; private set; }

        public void Report(string message)
        {
            StatusMessage = message;
            MessageReported?.Invoke(message);
        }

        public void Show(string message)
        {
            FloatingMessage = message;
            MessageRequested?.Invoke(message);
        }
    }
}
