using Launcher.Application.Accounts;

namespace Launcher.Infrastructure.Accounts;

internal sealed class MinecraftCapeService
{
    private readonly MicrosoftAuthProvider authProvider;
    private readonly MinecraftProfileClient profileClient;

    public MinecraftCapeService(
        MicrosoftAuthProvider authProvider,
        MinecraftProfileClient profileClient)
    {
        this.authProvider = authProvider;
        this.profileClient = profileClient;
    }

    public async Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(
        LauncherAccount account,
        CancellationToken cancellationToken)
    {
        var accessToken = await authProvider.GetAccessTokenAsync(account, cancellationToken);
        var profile = await profileClient.GetProfileAsync(accessToken, cancellationToken);
        var capes = profile.Capes ?? [];
        if (capes.Count == 0)
            return [];

        var options = new List<AccountCapeOption>
        {
            new()
            {
                Id = null,
                DisplayName = string.Empty,
                IsActive = capes.All(cape => !MinecraftAccountHelpers.IsActiveState(cape.State)),
                IsNone = true
            }
        };

        options.AddRange(capes.Select(cape => new AccountCapeOption
        {
            Id = cape.Id,
            DisplayName = string.IsNullOrWhiteSpace(cape.Alias) ? cape.Id ?? string.Empty : cape.Alias,
            ImageUrl = cape.Url,
            IsActive = MinecraftAccountHelpers.IsActiveState(cape.State),
            IsNone = false
        }));

        return options;
    }

    public async Task SetActiveCapeAsync(
        LauncherAccount account,
        string? capeId,
        CancellationToken cancellationToken)
    {
        var accessToken = await authProvider.GetAccessTokenAsync(account, cancellationToken);
        await profileClient.SetActiveCapeAsync(accessToken, capeId, cancellationToken);
    }
}
