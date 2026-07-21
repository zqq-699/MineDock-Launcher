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

public sealed class TerracottaAgreementDialogViewModelTests
{
    [Fact]
    public async Task InstalledModuleAllowsEntryWithoutCheckingForUpdates()
    {
        var context = Create(isAvailable: true);

        var result = await context.ViewModel.EnsureReadyAsync();

        Assert.True(result);
        Assert.False(context.ViewModel.IsOpen);
        Assert.Equal(0, context.Provisioning.EnsureCount);
    }

    [Fact]
    public async Task DownloadFailureKeepsNoticeOpenForRetry()
    {
        var context = Create();
        context.Provisioning.EnsureException = new IOException("Test failure.");
        var decision = context.ViewModel.EnsureReadyAsync();

        await context.ViewModel.AgreeCommand.ExecuteAsync(null);

        Assert.False(decision.IsCompleted);
        Assert.True(context.ViewModel.IsOpen);
        Assert.Equal(Strings.Dialog_TerracottaDownloadFailed, context.ViewModel.DownloadStatus);
        Assert.Equal(Strings.Status_TerracottaDownloadFailed, context.Messages.StatusMessage);
    }

    private static TestContext Create(bool isAvailable = false)
    {
        var provisioning = new RecordingProvisioningService { IsAvailable = isAvailable };
        var externalLinks = new RecordingExternalLinkService();
        var messages = new RecordingMessageService();
        var viewModel = new TerracottaAgreementDialogViewModel(
            provisioning,
            externalLinks,
            messages,
            messages);
        return new TestContext(viewModel, provisioning, externalLinks, messages);
    }

    private sealed record TestContext(
        TerracottaAgreementDialogViewModel ViewModel,
        RecordingProvisioningService Provisioning,
        RecordingExternalLinkService ExternalLinks,
        RecordingMessageService Messages);

    private sealed class RecordingProvisioningService : ITerracottaProvisioningService
    {
        public bool IsAvailable { get; set; }
        public int EnsureCount { get; private set; }
        public Exception? EnsureException { get; set; }

        public TerracottaModule? TryGetAvailable() => IsAvailable ? CreateModule() : null;

        public Task<TerracottaModule> EnsureAvailableAsync(
            IProgress<LauncherProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            EnsureCount++;
            if (EnsureException is not null)
                return Task.FromException<TerracottaModule>(EnsureException);
            IsAvailable = true;
            progress?.Report(new LauncherProgress("terracotta-ready", "Ready", 100));
            return Task.FromResult(CreateModule());
        }

        private static TerracottaModule CreateModule() => new(
            "0.4.2",
            "x86_64",
            "C:\\test",
            "C:\\test\\terracotta.exe");
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

        public void Report(string message)
        {
            StatusMessage = message;
            MessageReported?.Invoke(message);
        }

        public void Show(string message) => MessageRequested?.Invoke(message);
    }
}
