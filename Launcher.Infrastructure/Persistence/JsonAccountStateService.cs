using System.IO;
using System.Text.Json;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Infrastructure.Persistence;

public sealed class JsonAccountStateService : IAccountStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string accountStatePath;
    private readonly ILogger<JsonAccountStateService> logger;
    private readonly SemaphoreSlim ioLock = new(1, 1);

    public JsonAccountStateService(
        LauncherPathProvider? pathProvider = null,
        string? accountDataDirectory = null,
        ILogger<JsonAccountStateService>? logger = null)
    {
        var resolvedPathProvider = pathProvider ?? new LauncherPathProvider();
        var root = accountDataDirectory ?? resolvedPathProvider.DefaultAccountDataDirectory;
        accountStatePath = Path.Combine(root, "account-state.json");
        this.logger = logger ?? NullLogger<JsonAccountStateService>.Instance;
    }

    public async Task<LauncherAccountState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(accountStatePath))
            {
                var defaultState = Normalize(new LauncherAccountState());
                await SaveCoreAsync(defaultState, cancellationToken);
                logger.LogInformation("Default launcher account state created. AccountStatePath={AccountStatePath}", accountStatePath);
                return defaultState;
            }

            await using var stream = File.OpenRead(accountStatePath);
            var loaded = await JsonSerializer.DeserializeAsync<LauncherAccountState>(stream, JsonOptions, cancellationToken);
            var loadedState = Normalize(loaded ?? new LauncherAccountState());
            logger.LogDebug("Launcher account state loaded. AccountStatePath={AccountStatePath}", accountStatePath);
            return loadedState;
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task SaveAsync(LauncherAccountState state, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(state);
        await ioLock.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(normalized, cancellationToken);
            logger.LogInformation("Launcher account state saved. AccountStatePath={AccountStatePath}", accountStatePath);
        }
        finally
        {
            ioLock.Release();
        }
    }

    private async Task SaveCoreAsync(LauncherAccountState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(accountStatePath)!);
        await using var stream = File.Create(accountStatePath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
    }

    private LauncherAccountState Normalize(LauncherAccountState state)
    {
        if (string.IsNullOrWhiteSpace(state.OfflineUsername))
            state.OfflineUsername = LauncherDefaults.DefaultOfflineUsername;

        state.Accounts ??= [];
        state.Accounts.RemoveAll(account => string.IsNullOrWhiteSpace(account.Id)
            || string.IsNullOrWhiteSpace(account.DisplayName));
        foreach (var account in state.Accounts)
        {
            account.Capes ??= [];
            account.Capes.RemoveAll(cape => !cape.IsNone && string.IsNullOrWhiteSpace(cape.DisplayName));
            account.Skins ??= [];
            account.Skins.RemoveAll(skin => string.IsNullOrWhiteSpace(skin.Id)
                || string.IsNullOrWhiteSpace(skin.Source)
                || string.IsNullOrWhiteSpace(skin.ContentHash));
            if (!string.IsNullOrWhiteSpace(account.ActiveSkinId)
                && account.Skins.All(skin => !string.Equals(skin.Id, account.ActiveSkinId, StringComparison.Ordinal)))
            {
                account.ActiveSkinId = null;
            }
        }

        if (!state.AccountsInitialized)
            state.AccountsInitialized = true;

        if (!string.IsNullOrWhiteSpace(state.SelectedAccountId)
            && state.Accounts.All(account => !string.Equals(account.Id, state.SelectedAccountId, StringComparison.Ordinal)))
        {
            state.SelectedAccountId = null;
        }

        return state;
    }
}
