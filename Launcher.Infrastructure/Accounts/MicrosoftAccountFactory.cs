using CmlLib.Core.Auth.Microsoft.Sessions;
using Launcher.Application.Accounts;

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
        CancellationToken cancellationToken)
    {
        var uuid = MinecraftAccountHelpers.NormalizeUuid(profile.UUID);
        var avatarSource = await avatarService.GetOrCreateAvatarSourceAsync(
            uuid,
            MinecraftAccountHelpers.GetActiveSkinUrl(profile),
            forceRefreshAvatar,
            cancellationToken);
        var skinSource = await skinCacheService.GetOrCreateSkinSourceAsync(
            uuid,
            MinecraftAccountHelpers.GetActiveSkinUrl(profile),
            forceRefreshAvatar,
            cancellationToken);

        return new LauncherAccount
        {
            Id = $"microsoft-{uuid}",
            DisplayName = profile.Username ?? string.Empty,
            Uuid = uuid,
            AvatarSource = avatarSource,
            SkinSource = skinSource,
            IsOffline = false
        };
    }

    public async Task<LauncherAccount> CreateAccountFromProfileAsync(
        MinecraftProfileResponse profile,
        bool forceRefreshAvatar,
        CancellationToken cancellationToken)
    {
        var uuid = MinecraftAccountHelpers.NormalizeUuid(profile.Id);
        var avatarSource = await avatarService.GetOrCreateAvatarSourceAsync(
            uuid,
            MinecraftAccountHelpers.GetActiveSkinUrl(profile),
            forceRefreshAvatar,
            cancellationToken);
        var skinSource = await skinCacheService.GetOrCreateSkinSourceAsync(
            uuid,
            MinecraftAccountHelpers.GetActiveSkinUrl(profile),
            forceRefreshAvatar,
            cancellationToken);

        return new LauncherAccount
        {
            Id = $"microsoft-{uuid}",
            DisplayName = profile.Name ?? string.Empty,
            Uuid = uuid,
            AvatarSource = avatarSource,
            SkinSource = skinSource,
            SkinModel = MinecraftAccountHelpers.GetActiveSkinModel(profile),
            IsOffline = false,
            HasFreshProfile = true
        };
    }
}
