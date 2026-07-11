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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Account;

/// <summary>
/// 管理当前账户的皮肤库、轮播选择、皮肤模型切换及本地与远端状态同步。
/// </summary>
public sealed partial class AccountSkinLibraryViewModel : ObservableObject
{
    // 账户对象是持久化真相，ObservableCollection 是对话框当前会话的可观察投影。
    private readonly AccountListViewModel accountList;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly IAccountSkinLibraryService skinLibraryService;
    private readonly IAccountDialogService dialogService;
    private readonly IFilePickerService filePickerService;
    private readonly IMinecraftSkinFileValidator skinFileValidator;
    private readonly AccountProfileViewModel profile;
    private readonly ILogger logger;
    private LauncherSkinRecord? skinPendingModelChange;

    internal AccountSkinLibraryViewModel(
        AccountListViewModel accountList,
        IMicrosoftAccountService microsoftAccountService,
        IAccountSkinLibraryService skinLibraryService,
        AccountSkinModelDialogViewModel skinModelDialog,
        IAccountDialogService dialogService,
        IFilePickerService filePickerService,
        IMinecraftSkinFileValidator skinFileValidator,
        AccountProfileViewModel profile,
        ILogger logger)
    {
        this.accountList = accountList;
        this.microsoftAccountService = microsoftAccountService;
        this.skinLibraryService = skinLibraryService;
        SkinModelDialog = skinModelDialog;
        this.dialogService = dialogService;
        this.filePickerService = filePickerService;
        this.skinFileValidator = skinFileValidator;
        this.profile = profile;
        this.logger = logger;
        profile.PropertyChanged += (_, _) => NotifyCommandState();
    }

    public AccountSkinModelDialogViewModel SkinModelDialog { get; }

    [ObservableProperty]
    private LauncherSkinRecord? selectedSkin;

    [ObservableProperty]
    private bool isManagerDialogOpen;

    public ObservableCollection<LauncherSkinRecord> Skins { get; } = [];

    public LauncherSkinRecord? PreviousSkin => GetAdjacent(-1);

    public LauncherSkinRecord? NextSkin => GetAdjacent(1);

    public bool HasSkins => Skins.Count > 0;

    public bool IsOffline => accountList.SelectedAccount?.IsOffline == true;

    public bool IsThirdParty => accountList.SelectedAccount?.IsThirdParty == true;

    public bool CanShowStandardActions => !IsThirdParty;

    public bool CanShowThirdPartyRefresh => IsThirdParty;

    public bool HasPreview => !IsOffline && SelectedSkin is not null;

    public bool CanShowPreviewEmptyState => accountList.SelectedAccount is not null && !IsOffline && !HasPreview;

    public bool CanChangeSkin => accountList.SelectedAccount is { IsMicrosoft: true };

    public bool CanManageSkins => accountList.SelectedAccount is { IsMicrosoft: true };

    public bool CanApplySkin => accountList.SelectedAccount is { IsMicrosoft: true } account
        && !profile.IsBusy
        && SelectedSkin is { } skin
        && !IsAlreadyApplied(account, skin);

    public bool CanEditSelectedSkin => accountList.SelectedAccount is { IsMicrosoft: true }
        && !profile.IsBusy
        && SelectedSkin is not null;

    public bool CanDeleteSelectedSkin => CanEditSelectedSkin
        && accountList.SelectedAccount is { } account
        && SelectedSkin is { } skin
        && !string.Equals(account.ActiveSkinId, skin.Id, StringComparison.Ordinal);

    public bool CanShowManagerEmptyState => !HasSkins;

    public string? ActiveSkinId => accountList.SelectedAccount?.ActiveSkinId;

    public void SetAccount(LauncherAccount? account)
    {
        // 切换账户时按稳定皮肤 Id 重建集合，不能保留上一账户的 SelectedSkin 对象引用。
        skinPendingModelChange = null;
        if (account is null)
        {
            Skins.Clear();
            SelectedSkin = null;
            IsManagerDialogOpen = false;
        }
        else
        {
            Populate(account, account.ActiveSkinId);
        }
        NotifyState();
    }

    public async Task ConfirmSkinModelDialogAsync()
    {
        if (SkinModelDialog.IsSkinFormatError)
        {
            SkinModelDialog.Cancel();
            return;
        }
        if (!SkinModelDialog.TryConsumeSelection(out var path, out var model))
            return;
        if (skinPendingModelChange is { } pending)
        {
            skinPendingModelChange = null;
            await ChangeSkinModelAsync(pending, model);
            return;
        }
        await AddSkinAsync(path, model);
    }

    [RelayCommand]
    public async Task PickAndChangeSkinAsync()
    {
        // 文件选择与上传分开：用户取消不改变状态，选中后才进入统一新增流程。
        if (!CanChangeSkin)
            return;
        var path = filePickerService.PickMinecraftSkin();
        if (string.IsNullOrWhiteSpace(path))
            return;
        var validation = await skinFileValidator.ValidateAsync(path);
        if (validation.IsValid)
            dialogService.ShowSkinModelDialog(path);
        else
            dialogService.ShowSkinFormatErrorDialog();
    }

    [RelayCommand(CanExecute = nameof(CanManageSkins))]
    public void RequestOpenManagerDialog() => dialogService.ShowSkinManagerDialog();

    [RelayCommand]
    public void RequestCancelManagerDialog() => dialogService.CancelSkinManagerDialog();

    public void OpenManagerDialog()
    {
        if (CanManageSkins)
            IsManagerDialogOpen = true;
    }

    public void CloseManagerDialog() => IsManagerDialogOpen = false;

    [RelayCommand]
    public void SelectSkin(LauncherSkinRecord? skin)
    {
        if (skin is not null && Skins.Any(candidate => string.Equals(candidate.Id, skin.Id, StringComparison.Ordinal)))
            SelectedSkin = skin;
    }

    [RelayCommand(CanExecute = nameof(CanApplySkin))]
    public async Task ApplySkinAsync()
    {
        // 先完成服务端或本地应用，再整体替换账户记录；失败时 UI 仍指向原始可用账户。
        var account = accountList.SelectedAccount;
        var skin = SelectedSkin;
        if (account is null || skin is null || !CanApplySkin)
            return;
        var operation = profile.BeginOperation(account, Strings.Status_UploadingSkin);
        try
        {
            var uploaded = await microsoftAccountService.UploadSkinAsync(account, ResolveLocalPath(skin.Source), skin.SkinModel);
            if (!profile.IsCurrent(account, operation))
                return;
            var updated = AccountMapper.WithCapeCache(
                AccountMapper.WithSkinLibrary(
                    AccountMapper.WithAppearanceFallback(uploaded, account),
                    account.SkinLibrary,
                    skin.Id,
                    skin.Source,
                    skin.SkinModel),
                account.CachedCapeOptions);
            accountList.ReplaceSelectedAccount(account, updated);
            Populate(updated, skin.Id);
            await accountList.PersistAccountOrderAsync();
            profile.SetMessage(Strings.Status_SkinUpdated, showFloating: true);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Microsoft account skin apply failed. AccountId={AccountId} SkinId={SkinId}", account.Id, skin.Id);
            profile.SetError(exception, Strings.Status_SkinUpdateFailed, showFloating: true);
        }
        finally
        {
            profile.Complete(account, operation);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedSkin))]
    public void ChangeSelectedSkinModel()
    {
        if (SelectedSkin is not { } skin || !CanEditSelectedSkin)
            return;
        skinPendingModelChange = skin;
        dialogService.ShowSkinModelDialog(skin.SkinModel);
    }

    [RelayCommand(CanExecute = nameof(CanChangeSkinModel))]
    public void ChangeSkinModel(LauncherSkinRecord? skin)
    {
        if (!CanChangeSkinModel(skin))
            return;
        SelectedSkin = skin;
        ChangeSelectedSkinModel();
    }

    public bool CanChangeSkinModel(LauncherSkinRecord? skin) =>
        accountList.SelectedAccount is { IsMicrosoft: true } && !profile.IsBusy && skin is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedSkin))]
    public async Task DeleteSelectedSkinAsync()
    {
        // 删除前先决定相邻选择，因为删除后集合索引和轮播邻项都会变化。
        var account = accountList.SelectedAccount;
        var skin = SelectedSkin;
        if (account is null || skin is null || !CanDeleteSelectedSkin)
            return;
        var operation = profile.BeginOperation(account, string.Empty);
        try
        {
            await skinLibraryService.DeleteSkinAsync(account, skin);
            if (!profile.IsCurrent(account, operation))
                return;
            var preferredId = GetPreferredSkinIdAfterDelete(skin);
            var updated = AccountMapper.WithSkinLibrary(
                account,
                RemoveMatching(account.SkinLibrary, skin),
                account.ActiveSkinId,
                account.SkinSource,
                account.SkinModel);
            accountList.ReplaceSelectedAccount(account, updated);
            Populate(updated, preferredId);
            await accountList.PersistAccountOrderAsync();
            profile.SetMessage(Strings.Status_SkinDeleted);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Microsoft account skin delete failed. AccountId={AccountId} SkinId={SkinId}", account.Id, skin.Id);
            profile.SetError(exception, Strings.Status_SkinDeleteFailed);
        }
        finally
        {
            profile.Complete(account, operation);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSkin))]
    public async Task DeleteSkinAsync(LauncherSkinRecord? skin)
    {
        if (!CanDeleteSkin(skin))
            return;
        SelectedSkin = skin;
        await DeleteSelectedSkinAsync();
    }

    public bool CanDeleteSkin(LauncherSkinRecord? skin) =>
        accountList.SelectedAccount is { IsMicrosoft: true } account
        && !profile.IsBusy && skin is not null
        && !string.Equals(account.ActiveSkinId, skin.Id, StringComparison.Ordinal);

    [RelayCommand]
    public void RequestCancelModelDialog()
    {
        skinPendingModelChange = null;
        dialogService.CancelSkinModelDialog();
    }

    [RelayCommand]
    public Task RequestConfirmModelDialogAsync() => dialogService.ConfirmSkinModelDialogAsync();

    [RelayCommand(CanExecute = nameof(CanSelectPrevious))]
    public void SelectPrevious()
    {
        if (PreviousSkin is { } skin)
            SelectedSkin = skin;
    }

    public bool CanSelectPrevious() => PreviousSkin is not null;

    [RelayCommand(CanExecute = nameof(CanSelectNext))]
    public void SelectNext()
    {
        if (NextSkin is { } skin)
            SelectedSkin = skin;
    }

    public bool CanSelectNext() => NextSkin is not null;

    private async Task AddSkinAsync(string path, MinecraftSkinModel model)
    {
        // 服务返回规范化后的皮肤记录，本地路径、哈希或远端标识都以返回值为准。
        var account = accountList.SelectedAccount;
        if (account is null)
            return;
        if (!account.IsMicrosoft)
        {
            profile.SetMessage(Strings.Status_SkinOfflineUnsupported);
            return;
        }
        var operation = profile.BeginOperation(account, Strings.Status_AddingSkin);
        try
        {
            var imported = await skinLibraryService.ImportSkinAsync(account, path, model);
            if (!profile.IsCurrent(account, operation))
                return;
            var updated = AccountMapper.WithSkinLibrary(
                account,
                AddOrReplace(account.SkinLibrary, imported),
                account.ActiveSkinId,
                account.SkinSource,
                account.SkinModel);
            accountList.ReplaceSelectedAccount(account, updated);
            Populate(updated, imported.Id);
            await accountList.PersistAccountOrderAsync();
            profile.SetMessage(Strings.Status_SkinAdded);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Microsoft account skin import failed. AccountId={AccountId}", account.Id);
            profile.SetError(exception, Strings.Status_SkinUpdateFailed);
        }
        finally
        {
            profile.Complete(account, operation);
        }
    }

    private async Task ChangeSkinModelAsync(LauncherSkinRecord skin, MinecraftSkinModel model)
    {
        var account = accountList.SelectedAccount;
        if (account is null)
            return;
        var updatedSkin = CopyWithModel(skin, model);
        var updated = AccountMapper.WithSkinLibrary(
            account,
            ReplaceMatching(account.SkinLibrary, updatedSkin),
            account.ActiveSkinId,
            account.SkinSource,
            account.SkinModel);
        accountList.ReplaceSelectedAccount(account, updated);
        Populate(updated, updatedSkin.Id);
        await accountList.PersistAccountOrderAsync();
        profile.SetMessage(Strings.Status_SkinModelChanged);
    }

    private void Populate(LauncherAccount account, string? preferredId)
    {
        // 去重后一次性重建，确保同一内容不会因本地缓存和账户资料两个来源显示两次。
        Skins.Clear();
        if (account.IsThirdParty)
        {
            var activeSkin = account.SkinLibrary.FirstOrDefault(skin =>
                    string.Equals(skin.Id, account.ActiveSkinId, StringComparison.Ordinal))
                ?? account.SkinLibrary.FirstOrDefault(skin =>
                    string.Equals(skin.Source, account.SkinSource, StringComparison.Ordinal));
            if (activeSkin is not null)
                Skins.Add(activeSkin);
            SelectedSkin = activeSkin;
            NotifyState();
            return;
        }

        foreach (var skin in DistinctSkins(skinLibraryService.GetAvailableSkins(account)))
            Skins.Add(skin);
        SelectedSkin = Skins.FirstOrDefault(skin => string.Equals(skin.Id, preferredId, StringComparison.Ordinal))
            ?? Skins.FirstOrDefault(skin => string.Equals(skin.Id, account.ActiveSkinId, StringComparison.Ordinal))
            ?? Skins.FirstOrDefault();
        NotifyState();
    }

    partial void OnSelectedSkinChanged(LauncherSkinRecord? value) => NotifyState();

    private LauncherSkinRecord? GetAdjacent(int offset)
    {
        // 轮播在边界不循环；返回 null 会同时隐藏对应槽位并禁用命令。
        if (IsThirdParty || SelectedSkin is null || Skins.Count < 2)
            return null;
        var index = Skins.IndexOf(SelectedSkin) + offset;
        return index >= 0 && index < Skins.Count ? Skins[index] : null;
    }

    private void NotifyState()
    {
        // Previous/Next 是计算属性，选择或集合变化后必须成组通知 3D 控件更新三个槽位。
        OnPropertyChanged(nameof(PreviousSkin));
        OnPropertyChanged(nameof(NextSkin));
        OnPropertyChanged(nameof(HasSkins));
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(IsThirdParty));
        OnPropertyChanged(nameof(CanShowStandardActions));
        OnPropertyChanged(nameof(CanShowThirdPartyRefresh));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(CanShowPreviewEmptyState));
        OnPropertyChanged(nameof(CanChangeSkin));
        OnPropertyChanged(nameof(CanManageSkins));
        OnPropertyChanged(nameof(CanApplySkin));
        OnPropertyChanged(nameof(CanEditSelectedSkin));
        OnPropertyChanged(nameof(CanDeleteSelectedSkin));
        OnPropertyChanged(nameof(CanShowManagerEmptyState));
        OnPropertyChanged(nameof(ActiveSkinId));
        NotifyCommandState();
    }

    private void NotifyCommandState()
    {
        RequestOpenManagerDialogCommand.NotifyCanExecuteChanged();
        ApplySkinCommand.NotifyCanExecuteChanged();
        ChangeSelectedSkinModelCommand.NotifyCanExecuteChanged();
        DeleteSelectedSkinCommand.NotifyCanExecuteChanged();
        ChangeSkinModelCommand.NotifyCanExecuteChanged();
        DeleteSkinCommand.NotifyCanExecuteChanged();
        SelectPreviousCommand.NotifyCanExecuteChanged();
        SelectNextCommand.NotifyCanExecuteChanged();
    }

    private string? GetPreferredSkinIdAfterDelete(LauncherSkinRecord deleted)
    {
        var index = Skins.ToList().FindIndex(skin => string.Equals(skin.Id, deleted.Id, StringComparison.Ordinal));
        if (index + 1 < Skins.Count)
            return Skins[index + 1].Id;
        return index > 0 ? Skins[index - 1].Id : null;
    }

    private static string ResolveLocalPath(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : source;

    private static List<LauncherSkinRecord> AddOrReplace(IReadOnlyList<LauncherSkinRecord> skins, LauncherSkinRecord value)
    {
        var result = skins.ToList();
        var index = result.FindIndex(skin => string.Equals(skin.Id, value.Id, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(skin.ContentHash)
            && string.Equals(skin.ContentHash, value.ContentHash, StringComparison.OrdinalIgnoreCase)
            && skin.SkinModel == value.SkinModel);
        if (index >= 0)
            result[index] = value;
        else
            result.Add(value);
        return result;
    }

    private static List<LauncherSkinRecord> ReplaceMatching(IReadOnlyList<LauncherSkinRecord> skins, LauncherSkinRecord value) => skins
        .Select(skin => string.Equals(skin.Id, value.Id, StringComparison.Ordinal) ? value : skin)
        .ToList();

    private static List<LauncherSkinRecord> RemoveMatching(IReadOnlyList<LauncherSkinRecord> skins, LauncherSkinRecord removed) => skins
        .Where(skin => !string.Equals(skin.Id, removed.Id, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(skin.ContentHash)
                || !string.Equals(skin.ContentHash, removed.ContentHash, StringComparison.OrdinalIgnoreCase)
                || skin.SkinModel != removed.SkinModel))
        .ToList();

    private static LauncherSkinRecord CopyWithModel(LauncherSkinRecord skin, MinecraftSkinModel model) => new()
    {
        Id = skin.Id,
        Source = skin.Source,
        SkinModel = model,
        ContentHash = skin.ContentHash,
        AddedAtUtc = skin.AddedAtUtc
    };

    private static bool IsAlreadyApplied(LauncherAccount account, LauncherSkinRecord skin)
    {
        if (string.Equals(account.ActiveSkinId, skin.Id, StringComparison.Ordinal))
            return account.SkinModel == skin.SkinModel;
        var active = account.SkinLibrary.FirstOrDefault(value => string.Equals(value.Id, account.ActiveSkinId, StringComparison.Ordinal));
        if (active is not null && SameContent(active, skin))
            return true;
        return account.SkinModel == skin.SkinModel && string.Equals(account.SkinSource, skin.Source, StringComparison.Ordinal);
    }

    private static IEnumerable<LauncherSkinRecord> DistinctSkins(IEnumerable<LauncherSkinRecord> skins)
    {
        // 优先使用服务分配的 Id；历史记录缺少 Id 时回退到来源和模型构成的内容身份。
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skin in skins)
        {
            var key = string.IsNullOrWhiteSpace(skin.ContentHash)
                ? $"{skin.Source}|{skin.SkinModel}"
                : $"{skin.ContentHash}|{skin.SkinModel}";
            if (seen.Add(key))
                yield return skin;
        }
    }

    private static bool SameContent(LauncherSkinRecord left, LauncherSkinRecord right) =>
        left.SkinModel == right.SkinModel
        && (!string.IsNullOrWhiteSpace(left.ContentHash) && !string.IsNullOrWhiteSpace(right.ContentHash)
            ? string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase)
            : string.Equals(left.Source, right.Source, StringComparison.Ordinal));
}
