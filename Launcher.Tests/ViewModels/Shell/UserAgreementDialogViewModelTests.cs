/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Shell;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Shell;

public sealed class UserAgreementDialogViewModelTests
{
    private const string AgreementUrl = "https://docs.qq.com/markdown/DSmhwTHJ3WXVobHVY";

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void PrimeReflectsPersistedAcceptance(bool hasAccepted, bool expectedOpen)
    {
        var settings = new LauncherSettings { HasAcceptedUserAgreement = hasAccepted };
        var context = Create(settings);

        context.ViewModel.Prime(settings);

        Assert.Equal(expectedOpen, context.ViewModel.IsOpen);
        Assert.Equal(hasAccepted, context.ViewModel.WaitForDecisionAsync().IsCompleted);
    }

    [Fact]
    public async Task AgreePersistsAcceptanceBeforeReleasingStartup()
    {
        var settings = new LauncherSettings();
        var context = Create(settings);
        context.ViewModel.Prime(settings);

        await context.ViewModel.AgreeCommand.ExecuteAsync(null);

        Assert.True(settings.HasAcceptedUserAgreement);
        Assert.Equal(1, context.Settings.SaveCount);
        Assert.False(context.ViewModel.IsOpen);
        Assert.True(await context.ViewModel.WaitForDecisionAsync());
    }

    [Fact]
    public async Task SaveFailureKeepsDialogOpenAndStartupBlocked()
    {
        var settings = new LauncherSettings();
        var context = Create(settings);
        context.Settings.SaveException = new IOException("Test failure.");
        context.ViewModel.Prime(settings);

        await context.ViewModel.AgreeCommand.ExecuteAsync(null);

        Assert.False(settings.HasAcceptedUserAgreement);
        Assert.True(context.ViewModel.IsOpen);
        Assert.False(context.ViewModel.WaitForDecisionAsync().IsCompleted);
        Assert.Equal(Strings.Status_UserAgreementSaveFailed, context.Messages.StatusMessage);
        Assert.Equal(Strings.Status_UserAgreementSaveFailed, context.Messages.FloatingMessage);
    }

    [Fact]
    public void AgreementLinkUsesExpectedAddress()
    {
        var context = Create(new LauncherSettings());

        context.ViewModel.OpenAgreementCommand.Execute(null);

        Assert.Equal(AgreementUrl, context.ExternalLinks.LastUrl);
    }

    [Fact]
    public async Task DisagreeSignalsDeclineAndRequestsExit()
    {
        var context = Create(new LauncherSettings());

        context.ViewModel.DisagreeAndExitCommand.Execute(null);

        Assert.False(await context.ViewModel.WaitForDecisionAsync());
        Assert.True(context.ApplicationExit.WasRequested);
    }

    private static TestContext Create(LauncherSettings settings)
    {
        var settingsService = new RecordingSettingsService();
        var externalLinks = new RecordingExternalLinkService();
        var applicationExit = new RecordingApplicationExitService();
        var messages = new RecordingMessageService();
        var viewModel = new UserAgreementDialogViewModel(
            settingsService,
            externalLinks,
            applicationExit,
            messages,
            messages);
        return new TestContext(viewModel, settingsService, externalLinks, applicationExit, messages);
    }

    private sealed record TestContext(
        UserAgreementDialogViewModel ViewModel,
        RecordingSettingsService Settings,
        RecordingExternalLinkService ExternalLinks,
        RecordingApplicationExitService ApplicationExit,
        RecordingMessageService Messages);

    private sealed class RecordingSettingsService : ISettingsService
    {
        public int SaveCount { get; private set; }
        public Exception? SaveException { get; set; }

        public Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new LauncherSettings());

        public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return SaveException is null ? Task.CompletedTask : Task.FromException(SaveException);
        }
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

    private sealed class RecordingApplicationExitService : IApplicationExitService
    {
        public bool WasRequested { get; private set; }

        public void Shutdown() => WasRequested = true;
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
