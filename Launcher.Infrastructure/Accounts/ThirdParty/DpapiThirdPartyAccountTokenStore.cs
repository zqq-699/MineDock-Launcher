/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text.Json;
using Launcher.Application.Accounts;
using Launcher.Infrastructure.Accounts.Credentials;

namespace Launcher.Infrastructure.Accounts.ThirdParty;

internal sealed class DpapiThirdPartyAccountTokenStore : IThirdPartyAccountTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string credentialPath;
    private readonly SemaphoreSlim ioLock = new(1, 1);

    public DpapiThirdPartyAccountTokenStore(LauncherPathProvider pathProvider)
        : this(Path.Combine(pathProvider.DefaultAccountDataDirectory, "third-party", "credentials.dat"))
    {
    }

    internal DpapiThirdPartyAccountTokenStore(string credentialPath)
    {
        this.credentialPath = Path.GetFullPath(credentialPath);
    }

    public async Task<ThirdPartyAccountTokens?> GetAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            return entries.TryGetValue(accountId, out var tokens) ? tokens : null;
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task SaveAsync(
        string accountId,
        ThirdPartyAccountTokens tokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            entries[accountId] = tokens;
            await SaveCoreAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ioLock.Release();
        }
    }

    public async Task DeleteAsync(string accountId, CancellationToken cancellationToken = default)
    {
        await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (entries.Remove(accountId))
                await SaveCoreAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ioLock.Release();
        }
    }

    private async Task<Dictionary<string, ThirdPartyAccountTokens>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(credentialPath))
            return new Dictionary<string, ThirdPartyAccountTokens>(StringComparer.Ordinal);

        var protectedBytes = await File.ReadAllBytesAsync(credentialPath, cancellationToken).ConfigureAwait(false);
        var jsonBytes = WindowsDpapiProtector.Unprotect(protectedBytes);
        return JsonSerializer.Deserialize<Dictionary<string, ThirdPartyAccountTokens>>(jsonBytes, JsonOptions)
            ?? new Dictionary<string, ThirdPartyAccountTokens>(StringComparer.Ordinal);
    }

    private async Task SaveCoreAsync(
        Dictionary<string, ThirdPartyAccountTokens> entries,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(credentialPath)
            ?? throw new InvalidOperationException("Credential path must have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(credentialPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(entries, JsonOptions);
            var protectedBytes = WindowsDpapiProtector.Protect(jsonBytes);
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, credentialPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
