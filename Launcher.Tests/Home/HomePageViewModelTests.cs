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

using System.Windows;
using Launcher.App.Controls;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Accounts;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.Tests.Home;

public sealed class HomePageViewModelTests
{

    [Fact]
    public async Task HomePageSelectLaunchInstanceCommandPersistsAndSelectsInstance()
    {
        GameInstance? requestedInstance = null;
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(
            statusService,
            instance =>
            {
                requestedInstance = instance;
                return Task.FromResult(true);
            });
        var first = CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla);
        var second = CreateInstance("second", "Fabric Pack", "1.21.1", LoaderKind.Fabric);
        viewModel.SetLaunchInstances([first, second]);

        await viewModel.SelectLaunchInstanceCommand.ExecuteAsync(viewModel.LaunchInstances[1]);

        Assert.Same(second, requestedInstance);
        Assert.Same(second, viewModel.SelectedInstance);
        Assert.False(viewModel.LaunchInstances[0].IsSelected);
        Assert.True(viewModel.LaunchInstances[1].IsSelected);
        Assert.True(viewModel.HasSelectedLaunchInstance);
        Assert.Same(viewModel.LaunchInstances[1], viewModel.SelectedLaunchInstanceItem);
        Assert.Equal(
            string.Format(Strings.Status_LaunchInstanceSelectedFormat, "Fabric Pack"),
            statusService.LastMessage);
    }

    [Fact]
    public async Task HomePageSelectLaunchInstanceCommandSelectsInstanceBeforePersistenceCompletes()
    {
        var persistenceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var persistenceRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(
            selectLaunchInstance: _ =>
            {
                persistenceStarted.SetResult();
                return persistenceRelease.Task;
            });
        var first = CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla);
        var second = CreateInstance("second", "Fabric Pack", "1.21.1", LoaderKind.Fabric);
        viewModel.SetLaunchInstances([first, second]);

        var selectTask = viewModel.SelectLaunchInstanceCommand.ExecuteAsync(viewModel.LaunchInstances[1]);
        await persistenceStarted.Task;

        Assert.Same(second, viewModel.SelectedInstance);
        Assert.False(viewModel.LaunchInstances[0].IsSelected);
        Assert.True(viewModel.LaunchInstances[1].IsSelected);
        Assert.Same(viewModel.LaunchInstances[1], viewModel.SelectedLaunchInstanceItem);

        persistenceRelease.SetResult(true);
        await selectTask;
    }

    [Fact]
    public async Task HomePageToggleLaunchMenuPinnedCommandPersistsPreference()
    {
        bool? requestedValue = null;
        var viewModel = CreateViewModel(
            setLaunchMenuPinned: value =>
            {
                requestedValue = value;
                return Task.FromResult(true);
            });

        await viewModel.ToggleLaunchMenuPinnedCommand.ExecuteAsync(null);

        Assert.True(requestedValue.HasValue);
        Assert.True(requestedValue.Value);
        Assert.True(viewModel.IsLaunchMenuPinned);
        Assert.Equal(Strings.Home_UnpinLaunchMenuTooltip, viewModel.LaunchGames.LaunchMenuPinTooltip);
    }

    [Fact]
    public async Task HomePageToggleLaunchMenuPinnedCommandRollsBackWhenPreferenceSaveFails()
    {
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(
            statusService,
            setLaunchMenuPinned: _ => Task.FromResult(false));

        await viewModel.ToggleLaunchMenuPinnedCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsLaunchMenuPinned);
        Assert.Equal(Strings.Home_PinLaunchMenuTooltip, viewModel.LaunchGames.LaunchMenuPinTooltip);
        Assert.Equal(Strings.Status_SettingsSaveFailed, statusService.LastMessage);
    }

    [Fact]
    public async Task HomePageSelectLaunchInstanceCommandShowsFriendlyFailure()
    {
        var statusService = new FakeStatusService();
        var viewModel = CreateViewModel(statusService, _ => Task.FromResult(false));
        var instance = CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla);
        viewModel.SetLaunchInstances([instance]);

        await viewModel.SelectLaunchInstanceCommand.ExecuteAsync(viewModel.LaunchInstances[0]);

        Assert.Null(viewModel.SelectedInstance);
        Assert.False(viewModel.LaunchInstances[0].IsSelected);
        Assert.False(viewModel.HasSelectedLaunchInstance);
        Assert.Null(viewModel.SelectedLaunchInstanceItem);
        Assert.Equal(Strings.Status_LaunchInstanceSelectionFailed, statusService.LastMessage);
    }

    [Fact]
    public void HomePageShowsEmptyLaunchInstanceState()
    {
        var viewModel = CreateViewModel();

        viewModel.SetLaunchInstances([]);

        Assert.Empty(viewModel.LaunchInstances);
        Assert.False(viewModel.HasLaunchInstances);
        Assert.True(viewModel.HasNoLaunchInstances);
        Assert.False(viewModel.HasSelectedLaunchInstance);
        Assert.Null(viewModel.SelectedLaunchInstanceItem);
    }

    [Fact]
    public async Task HomePageLaunchPassesSelectedAccountToLaunchService()
    {
        var launchService = new FakeLaunchService();
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
        var viewModel = CreateViewModel(launchService: launchService, selectedAccount: account);
        var instance = CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla);
        viewModel.SetSelectedInstance(instance);

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Same(instance, launchService.LastInstance);
        Assert.Same(account, launchService.LastAccount);
    }

    [Fact]
    public async Task HomePageLaunchShowsProgressDuringLaunchAndResetsAfterSuccess()
    {
        var releaseLaunch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var statusService = new FakeStatusService();
        var floatingMessageService = new FakeFloatingMessageService();
        var launchService = new FakeLaunchService
        {
            LaunchBehavior = async (progress, cancellationToken) =>
            {
                progress!.Report(new LauncherProgress(LaunchProgressStages.RunningPreLaunchCommand, "ignored", 4));
                await Task.Delay(25, cancellationToken);
                await releaseLaunch.Task;
            }
        };
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
        var viewModel = CreateViewModel(
            statusService,
            launchService: launchService,
            selectedAccount: account,
            floatingMessageService: floatingMessageService);
        viewModel.SetSelectedInstance(CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla));

        var launchTask = viewModel.LaunchCommand.ExecuteAsync(null);
        await Task.Delay(60);

        Assert.True(viewModel.IsLaunching);
        Assert.True(viewModel.HasLaunchProgress);
        Assert.False(viewModel.CanLaunchSelectedGame);
        Assert.False(viewModel.LaunchCommand.CanExecute(null));
        Assert.Equal(Strings.Status_LaunchRunningPreLaunchCommand, viewModel.LaunchStatusMessage);
        Assert.Equal(4, viewModel.LaunchProgressPercent);
        Assert.False(viewModel.HasLaunchDownloadSpeedText);
        Assert.Equal(string.Empty, viewModel.LaunchDownloadSpeedText);
        Assert.Equal(Strings.Status_LaunchRunningPreLaunchCommand, statusService.LastMessage);

        releaseLaunch.SetResult();
        await launchTask;

        Assert.False(viewModel.IsLaunching);
        Assert.False(viewModel.HasLaunchProgress);
        Assert.True(viewModel.CanLaunchSelectedGame);
        Assert.Equal(string.Empty, viewModel.LaunchStatusMessage);
        Assert.Equal(string.Empty, viewModel.LaunchDownloadSpeedText);
        Assert.Equal(0, viewModel.LaunchProgressPercent);
    }

    [Fact]
    public async Task HomePageCancelLaunchCancelsCurrentLaunchAndResetsProgress()
    {
        var statusService = new FakeStatusService();
        var floatingMessageService = new FakeFloatingMessageService();
        var launchService = new FakeLaunchService
        {
            LaunchBehavior = async (progress, cancellationToken) =>
            {
                progress!.Report(new LauncherProgress(LaunchProgressStages.CheckingInstance, "ignored", 12));
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
        var viewModel = CreateViewModel(
            statusService,
            launchService: launchService,
            selectedAccount: account,
            floatingMessageService: floatingMessageService);
        viewModel.SetSelectedInstance(CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla));

        var launchTask = viewModel.LaunchCommand.ExecuteAsync(null);
        await Task.Delay(60);

        Assert.True(viewModel.CancelLaunchCommand.CanExecute(null));
        viewModel.CancelLaunchCommand.Execute(null);
        await launchTask;

        Assert.Equal(Strings.Status_LaunchCanceled, statusService.LastMessage);
        Assert.Equal(Strings.Status_LaunchCanceled, floatingMessageService.LastMessage);
        Assert.False(viewModel.IsLaunching);
        Assert.False(viewModel.HasLaunchProgress);
        Assert.False(viewModel.CancelLaunchCommand.CanExecute(null));
        Assert.Equal(string.Empty, viewModel.LaunchStatusMessage);
        Assert.Equal(0, viewModel.LaunchProgressPercent);
    }

    [Fact]
    public async Task HomePageLaunchShowsFriendlyAccountFailure()
    {
        var statusService = new FakeStatusService();
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new LaunchAccountSessionException()
        };
        var account = new LauncherAccount
        {
            Id = "microsoft-1",
            DisplayName = "LiveUser",
            Uuid = "00000000000000000000000000000001",
            IsOffline = false
        };
        var viewModel = CreateViewModel(statusService, launchService: launchService, selectedAccount: account);
        viewModel.SetSelectedInstance(CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla));

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_LaunchAccountUnavailable, statusService.LastMessage);
        Assert.False(viewModel.IsLaunching);
        Assert.False(viewModel.HasLaunchProgress);
        Assert.Equal(string.Empty, viewModel.LaunchStatusMessage);
        Assert.Equal(0, viewModel.LaunchProgressPercent);
    }

    [Fact]
    public async Task HomePageLaunchShowsFriendlyRepairFailure()
    {
        var statusService = new FakeStatusService();
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new InstanceRepairException("repair failed")
        };
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
        var viewModel = CreateViewModel(statusService, launchService: launchService, selectedAccount: account);
        viewModel.SetSelectedInstance(CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla));

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_LaunchInstanceRepairFailed, statusService.LastMessage);
        Assert.False(viewModel.IsLaunching);
        Assert.False(viewModel.HasLaunchProgress);
        Assert.Equal(string.Empty, viewModel.LaunchStatusMessage);
        Assert.Equal(0, viewModel.LaunchProgressPercent);
    }

    [Fact]
    public async Task HomePageLaunchRaisesJavaRequirementEventForAutomaticRuntimeNotFound()
    {
        var statusService = new FakeStatusService();
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new JavaRuntimeSelectionException(
                "missing java",
                JavaRuntimeSelectionFailureReason.AutomaticRuntimeNotFound,
                21)
        };
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
        var viewModel = CreateViewModel(statusService, launchService: launchService, selectedAccount: account);
        viewModel.SetSelectedInstance(CreateInstance("first", "First World", "1.20.5", LoaderKind.Vanilla));
        JavaRequirementNotMetEventArgs? eventArgs = null;
        viewModel.JavaRequirementNotMet += (_, args) => eventArgs = args;

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_JavaSelectionFailed, statusService.LastMessage);
        Assert.NotNull(eventArgs);
        Assert.Equal(21, eventArgs.RequiredMajorVersion);
        Assert.Equal(JavaRuntimeSelectionFailureReason.AutomaticRuntimeNotFound, eventArgs.Reason);
        Assert.False(viewModel.IsLaunching);
    }

    [Fact]
    public async Task HomePageLaunchRaisesJavaRequirementEventForAutomaticRuntimeMissing()
    {
        var statusService = new FakeStatusService();
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new JavaRuntimeSelectionException(
                "missing java",
                JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing,
                21)
        };
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
        var viewModel = CreateViewModel(statusService, launchService: launchService, selectedAccount: account);
        viewModel.SetSelectedInstance(CreateInstance("first", "First World", "1.20.5", LoaderKind.Vanilla));
        JavaRequirementNotMetEventArgs? eventArgs = null;
        viewModel.JavaRequirementNotMet += (_, args) => eventArgs = args;

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_JavaSelectionFailed, statusService.LastMessage);
        Assert.NotNull(eventArgs);
        Assert.Equal(21, eventArgs.RequiredMajorVersion);
        Assert.Equal(JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing, eventArgs.Reason);
        Assert.False(viewModel.IsLaunching);
    }

    [Fact]
    public async Task HomePageLaunchRaisesJavaRequirementEventForManualVersionMismatch()
    {
        var statusService = new FakeStatusService();
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new JavaRuntimeSelectionException(
                "manual java too low",
                JavaRuntimeSelectionFailureReason.ManualRuntimeVersionTooLow,
                21,
                8)
        };
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
        var instance = CreateInstance("first", "First World", "1.20.5", LoaderKind.Vanilla);
        var viewModel = CreateViewModel(statusService, launchService: launchService, selectedAccount: account);
        viewModel.SetSelectedInstance(instance);
        JavaRequirementNotMetEventArgs? eventArgs = null;
        var launchFailureReported = false;
        viewModel.JavaRequirementNotMet += (_, args) => eventArgs = args;
        viewModel.LaunchFailureReported += (_, _) => launchFailureReported = true;

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_JavaSelectionFailed, statusService.LastMessage);
        Assert.NotNull(eventArgs);
        Assert.Same(instance, eventArgs.Instance);
        Assert.Equal(21, eventArgs.RequiredMajorVersion);
        Assert.Equal(8, eventArgs.CurrentMajorVersion);
        Assert.Equal(JavaRuntimeSelectionFailureReason.ManualRuntimeVersionTooLow, eventArgs.Reason);
        Assert.False(launchFailureReported);
        Assert.False(viewModel.IsLaunching);
    }

    [Fact]
    public async Task HomePageForceLaunchIgnoresJavaVersionRequirement()
    {
        var statusService = new FakeStatusService();
        var launchService = new FakeLaunchService();
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
        var instance = CreateInstance("first", "First World", "1.20.5", LoaderKind.Vanilla);
        var viewModel = CreateViewModel(statusService, launchService: launchService, selectedAccount: account);

        await viewModel.ForceLaunchIgnoringJavaRequirementAsync(instance);

        Assert.Equal(1, launchService.LaunchCallCount);
        Assert.Same(instance, launchService.LastInstance);
        Assert.True(launchService.LastOptions?.IgnoreJavaVersionRequirement);
        Assert.False(viewModel.IsLaunching);
    }

    [Fact]
    public async Task HomePageLaunchShowsFriendlyQuickExitFailure()
    {
        var statusService = new FakeStatusService();
        var report = new LaunchFailureReport(
            LaunchFailureKind.StartupProcessExited,
            "First World",
            "1.20.1",
            0,
            @"C:\temp\diagnostic.log",
            @"C:\temp");
        var launchService = new FakeLaunchService
        {
            ExceptionToThrow = new LaunchProcessExitedException(report)
        };
        var account = new LauncherAccount
        {
            Id = "offline-1",
            DisplayName = "LocalUser",
            Uuid = "00000000-0000-0000-0000-000000000001",
            IsOffline = true
        };
        var viewModel = CreateViewModel(statusService, launchService: launchService, selectedAccount: account);
        viewModel.SetSelectedInstance(CreateInstance("first", "First World", "1.20.1", LoaderKind.Vanilla));

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Status_LaunchProcessExited, statusService.LastMessage);
        Assert.False(viewModel.IsLaunching);
        Assert.False(viewModel.HasLaunchProgress);
        Assert.Equal(string.Empty, viewModel.LaunchStatusMessage);
        Assert.Equal(0, viewModel.LaunchProgressPercent);
    }

    private static HomePageViewModel CreateViewModel(
        FakeStatusService? statusService = null,
        Func<GameInstance, Task<bool>>? selectLaunchInstance = null,
        IReadOnlyList<MinecraftVersionInfo>? versions = null,
        FakeLaunchService? launchService = null,
        LauncherAccount? selectedAccount = null,
        FakeWindowService? windowService = null,
        FakeFloatingMessageService? floatingMessageService = null,
        Func<bool, Task<bool>>? setLaunchMenuPinned = null)
    {
        statusService ??= new FakeStatusService();
        return new HomePageViewModel(
            launchService ?? new FakeLaunchService(),
            new FakeGameVersionService(versions ?? []),
            CreateAccountPage(statusService, selectedAccount),
            statusService,
            floatingMessageService ?? new FakeFloatingMessageService(),
            windowService ?? new FakeWindowService(),
            ImmediateUiDispatcher.Instance,
            _ => { },
            selectLaunchInstance ?? (_ => Task.FromResult(true)),
            setLaunchMenuPinned ?? (_ => Task.FromResult(true)),
            _ => Task.CompletedTask);
    }

    private static AccountPageViewModel CreateAccountPage(
        FakeStatusService statusService,
        LauncherAccount? selectedAccount = null)
    {
        var accountList = new AccountListViewModel(new FakeAccountStore());
        var microsoftAccountService = new FakeMicrosoftAccountService();
        var offlineUuidService = new FakeOfflineAccountUuidService();
        if (selectedAccount is not null)
        {
            accountList.Accounts.Add(selectedAccount);
            accountList.SelectAccount(selectedAccount);
        }

        var accountDialogService = new FakeAccountDialogService();
        var accountSkinModelDialog = new AccountSkinModelDialogViewModel();
        return new AccountPageViewModel(
            accountList,
            new AccountDialogViewModel(accountList, microsoftAccountService, offlineUuidService, statusService),
            new AccountAppearanceViewModel(
                accountList,
                microsoftAccountService,
                new FakeAccountSkinLibraryService(),
                accountSkinModelDialog,
                accountDialogService,
                new FakeFilePickerService(),
                new FakeSkinFileValidator()),
            new AccountOfflineUuidViewModel(
                accountList,
                offlineUuidService,
                statusService,
                new FakeClipboardService()),
            accountDialogService);
    }

    private static GameInstance CreateInstance(
        string id,
        string name,
        string minecraftVersion,
        LoaderKind loader,
        DateTimeOffset? createdAt = null)
    {
        return new GameInstance
        {
            Id = id,
            Name = name,
            MinecraftVersion = minecraftVersion,
            VersionName = minecraftVersion,
            Loader = loader,
            CreatedAt = createdAt ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private sealed class FakeLaunchService : ILaunchService
    {
        public GameInstance? LastInstance { get; private set; }
        public LauncherAccount? LastAccount { get; private set; }
        public LaunchRequestOptions? LastOptions { get; private set; }
        public int LaunchCallCount { get; private set; }
        public Exception? ExceptionToThrow { get; init; }
        public Func<IProgress<LauncherProgress>?, CancellationToken, Task>? LaunchBehavior { get; init; }

        public async Task<GameLaunchSession> LaunchAsync(
            GameInstance instance,
            LauncherAccount account,
            LauncherSettings settings,
            IProgress<LauncherProgress>? progress,
            LaunchRequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LaunchCallCount++;
            LastInstance = instance;
            LastAccount = account;
            LastOptions = options;
            if (LaunchBehavior is not null)
            {
                await LaunchBehavior(progress, cancellationToken);
                return CreateSuccessfulSession(instance);
            }

            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            return CreateSuccessfulSession(instance);
        }

        private static GameLaunchSession CreateSuccessfulSession(GameInstance instance)
        {
            return new GameLaunchSession(
                instance.Id,
                instance.Name,
                Task.FromResult(LaunchExitResult.Success));
        }
    }

    private sealed class FakeStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public string? LastMessage { get; private set; }

        public void Report(string message)
        {
            LastMessage = message;
            MessageReported?.Invoke(message);
        }
    }

    private sealed class FakeFloatingMessageService : IFloatingMessageService
    {
        public event Action<string>? MessageRequested;

        public string? LastMessage { get; private set; }

        public void Show(string message)
        {
            LastMessage = message;
            MessageRequested?.Invoke(message);
        }
    }

    private sealed class FakeAccountStore : IAccountStore
    {
        public Task<AccountStoreSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AccountStoreSnapshot([], null));
        }

        public Task SaveOrderAsync(
            string? selectedAccountId,
            IEnumerable<LauncherAccount> accounts,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMicrosoftAccountService : IMicrosoftAccountService
    {
        public Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LauncherAccount>>([]);
        }

        public Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(LauncherAccount account, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<LauncherAccount> RefreshAccountProfileAsync(
            LauncherAccount account,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<LauncherAccount> UploadSkinAsync(
            LauncherAccount account,
            string skinFilePath,
            MinecraftSkinModel skinModel,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SetActiveCapeAsync(LauncherAccount account, string? capeId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<LauncherAccount> ChangeNameAsync(LauncherAccount account, string newName, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeAccountDialogService : IAccountDialogService
    {
        public void Attach(
            AccountPageViewModel accountPage,
            DialogHost addAccountHost,
            DialogHost deleteAccountHost,
            DialogHost renameAccountHost,
            DialogHost skinModelDialogHost,
            DialogHost skinManagerDialogHost)
        {
        }

        public void ShowAddAccountDialog()
        {
        }

        public void ShowDeleteAccountDialog(LauncherAccount account)
        {
        }

        public void ShowRenameAccountDialog()
        {
        }

        public void ShowSkinModelDialog(string skinFilePath)
        {
        }

        public void ShowSkinModelDialog(MinecraftSkinModel skinModel)
        {
        }

        public void ShowSkinFormatErrorDialog()
        {
        }

        public void ShowSkinManagerDialog()
        {
        }

        public void CancelAddAccountDialog()
        {
        }

        public void BackAddAccountDialog()
        {
        }

        public Task ConfirmAddAccountDialogAsync()
        {
            throw new NotSupportedException();
        }

        public void CancelDeleteAccountDialog()
        {
        }

        public Task ConfirmDeleteAccountDialogAsync()
        {
            throw new NotSupportedException();
        }

        public void CancelRenameAccountDialog()
        {
        }

        public Task ConfirmRenameAccountDialogAsync()
        {
            throw new NotSupportedException();
        }

        public void CancelSkinModelDialog()
        {
        }

        public Task ConfirmSkinModelDialogAsync()
        {
            throw new NotSupportedException();
        }

        public void CancelSkinManagerDialog()
        {
        }

        public void Prewarm()
        {
        }
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public void CopyText(string text)
        {
        }
    }

    private sealed class FakeWindowService : IWindowService
    {
        public int MinimizeCallCount { get; private set; }

        public void Attach(Window window)
        {
        }

        public void Minimize()
        {
            MinimizeCallCount++;
        }

        public void RestoreAndActivate()
        {
        }

        public void Close()
        {
        }
    }

    private sealed class FakeFilePickerService : IFilePickerService
    {
        public string? PickMinecraftSkin()
        {
            return null;
        }

        public string? PickJavaExecutable()
        {
            return null;
        }

        public string? PickModFile()
        {
            return null;
        }

        public string? PickSaveArchive()
        {
            return null;
        }

        public string? PickResourcePackArchive()
        {
            return null;
        }

        public string? PickShaderPackArchive()
        {
            return null;
        }

        public string? PickModpackExportArchive(string defaultFileName, ModpackExportKind kind)
        {
            return null;
        }

        public string? PickLocalImportFile()
        {
            return null;
        }

        public string? PickFolder(string title, string? initialDirectory = null)
        {
            return null;
        }

    }

    private sealed class FakeSkinFileValidator : IMinecraftSkinFileValidator
    {
        public Task<MinecraftSkinFileValidationResult> ValidateAsync(
            string skinFilePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MinecraftSkinFileValidationResult(true, 64, 64));
        }
    }
}


