using CmlLib.Core.Auth.Microsoft.Sessions;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Accounts;

internal sealed class MicrosoftAccountFactory
{
    private readonly AccountAvatarService avatarService;
    private readonly AccountSkinCacheService skinCacheService;

    public MicrosoftAccountFactory(
        AccountAvatarService avatarService,
        AccountSkinCacheService skinCacheService)
    {
        this.avatarService = avatarService;
        this.skinCacheService = skinCacheService;
    }

    public async Task<LauncherAccount> CreateAccountFromProfileAsync(
        JEProfile profile,
        bool forceRefreshAvatar,
        CancellationToken cancellationToken,
        IReadOnlyList<LauncherSkinRecord>? existingSkins = null)
    {
        var uuid = MinecraftAccountHelpers.NormalizeUuid(profile.UUID);
        var skinUrl = MinecraftAccountHelpers.GetActiveSkinUrl(profile);
        var avatarSource = await avatarService.GetOrCreateAvatarSourceAsync(
            uuid,
            skinUrl,
            forceRefreshAvatar,
            cancellationToken);
        var skin = await skinCacheService.GetOrCreateSkinRecordFromUrlAsync(
            uuid,
            skinUrl,
            MinecraftSkinModel.Classic,
            existingSkins ?? [],
            forceRefreshAvatar,
            cancellationToken);
        var skins = MergeSkinLibrary(existingSkins ?? [], skin);
        var skinSource = skin?.Source;

        return new LauncherAccount
        {
            Id = $"microsoft-{uuid}",
            DisplayName = profile.Username ?? string.Empty,
            Uuid = uuid,
            AvatarSource = avatarSource,
            SkinSource = skinSource,
            SkinModel = skin?.SkinModel,
            SkinLibrary = skins,
            ActiveSkinId = skin?.Id,
            IsOffline = false
        };
    }

    public async Task<LauncherAccount> CreateAccountFromProfileAsync(
        MinecraftProfileResponse profile,
        bool forceRefreshAvatar,
        CancellationToken cancellationToken,
        IReadOnlyList<LauncherSkinRecord>? existingSkins = null)
    {
        var uuid = MinecraftAccountHelpers.NormalizeUuid(profile.Id);
        var skinUrl = MinecraftAccountHelpers.GetActiveSkinUrl(profile);
        var skinModel = MinecraftAccountHelpers.GetActiveSkinModel(profile) ?? MinecraftSkinModel.Classic;
        var avatarSource = await avatarService.GetOrCreateAvatarSourceAsync(
            uuid,
            skinUrl,
            forceRefreshAvatar,
            cancellationToken);
        var skin = await skinCacheService.GetOrCreateSkinRecordFromUrlAsync(
            uuid,
            skinUrl,
            skinModel,
            existingSkins ?? [],
            forceRefreshAvatar,
            cancellationToken);
        var skins = MergeSkinLibrary(existingSkins ?? [], skin);
        var skinSource = skin?.Source;

        return new LauncherAccount
        {
            Id = $"microsoft-{uuid}",
            DisplayName = profile.Name ?? string.Empty,
            Uuid = uuid,
            AvatarSource = avatarSource,
            SkinSource = skinSource,
            SkinModel = skin?.SkinModel,
            SkinLibrary = skins,
            ActiveSkinId = skin?.Id,
            IsOffline = false,
            HasFreshProfile = true
        };
    }

    private static List<LauncherSkinRecord> MergeSkinLibrary(
        IReadOnlyList<LauncherSkinRecord> existingSkins,
        LauncherSkinRecord? activeSkin)
    {
        var skins = existingSkins.Select(skin => new LauncherSkinRecord
        {
            Id = skin.Id,
            Source = skin.Source,
            SkinModel = skin.SkinModel,
            ContentHash = skin.ContentHash,
            AddedAtUtc = skin.AddedAtUtc
        }).ToList();

        if (activeSkin is null)
            return skins;

        var index = skins.FindIndex(skin =>
            skin.SkinModel == activeSkin.SkinModel
            && string.Equals(skin.ContentHash, activeSkin.ContentHash, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            skins[index] = activeSkin;
        else
            skins.Add(activeSkin);

        return skins;
    }
}
