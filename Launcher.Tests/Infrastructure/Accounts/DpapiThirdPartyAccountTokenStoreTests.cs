/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Text;
using Launcher.Application.Accounts;
using Launcher.Infrastructure.Accounts.ThirdParty;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class DpapiThirdPartyAccountTokenStoreTests
{
    [Fact]
    public async Task SavesTokensEncryptedAndSupportsOverwriteAndDelete()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"launcher-third-party-token-test-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "credentials.dat");
        try
        {
            var store = new DpapiThirdPartyAccountTokenStore(path);
            await store.SaveAsync("account", new ThirdPartyAccountTokens("access-secret", "client-secret"));

            var diskText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path));
            Assert.DoesNotContain("access-secret", diskText, StringComparison.Ordinal);
            Assert.DoesNotContain("client-secret", diskText, StringComparison.Ordinal);
            Assert.Equal(
                new ThirdPartyAccountTokens("access-secret", "client-secret"),
                await store.GetAsync("account"));

            await store.SaveAsync("account", new ThirdPartyAccountTokens("new-access", "new-client"));
            Assert.Equal(new ThirdPartyAccountTokens("new-access", "new-client"), await store.GetAsync("account"));

            await store.DeleteAsync("account");
            Assert.Null(await store.GetAsync("account"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
