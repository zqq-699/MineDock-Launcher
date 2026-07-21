/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Text;
using System.Text.Json.Nodes;
using Launcher.Infrastructure.Accounts.Credentials;

namespace Launcher.Tests.Infrastructure.Accounts;

public sealed class DpapiMicrosoftJsonStorageTests
{
    [Fact]
    public void SavesMicrosoftSessionsEncryptedAndSupportsOverwrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"launcher-microsoft-token-test-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "credentials.dat");
        try
        {
            var storage = new DpapiMicrosoftJsonStorage(path);
            storage.Write(new JsonObject
            {
                ["account"] = new JsonObject { ["refreshToken"] = "refresh-secret" }
            }, null);

            var diskText = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            Assert.DoesNotContain("refresh-secret", diskText, StringComparison.Ordinal);
            Assert.Equal(
                "refresh-secret",
                storage.ReadAsJsonNode()?["account"]?["refreshToken"]?.GetValue<string>());

            storage.Write(new JsonObject
            {
                ["account"] = new JsonObject { ["refreshToken"] = "replacement-secret" }
            }, null);
            Assert.Equal(
                "replacement-secret",
                storage.ReadAsJsonNode()?["account"]?["refreshToken"]?.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IgnoresLegacyPlaintextFileAndDoesNotModifyIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"launcher-microsoft-legacy-test-{Guid.NewGuid():N}");
        var credentialPath = Path.Combine(directory, "credentials.dat");
        var legacyPath = Path.Combine(directory, "accounts.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(legacyPath, "legacy-plaintext-token");
            var storage = new DpapiMicrosoftJsonStorage(credentialPath);

            Assert.Null(storage.ReadAsJsonNode());
            Assert.Equal("legacy-plaintext-token", File.ReadAllText(legacyPath));
            Assert.False(File.Exists(credentialPath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

}
