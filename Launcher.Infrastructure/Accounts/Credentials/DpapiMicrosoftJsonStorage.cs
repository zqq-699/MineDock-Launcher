/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using XboxAuthNet.Game.Accounts.JsonStorage;

namespace Launcher.Infrastructure.Accounts.Credentials;

/// <summary>
/// Stores CmlLib account sessions as one DPAPI CurrentUser protected payload.
/// The legacy plaintext accounts.json file is intentionally not read or modified.
/// </summary>
internal sealed class DpapiMicrosoftJsonStorage : IJsonStorage
{
    private readonly string credentialPath;
    private readonly object ioLock = new();

    public DpapiMicrosoftJsonStorage(LauncherPathProvider pathProvider)
        : this(Path.Combine(pathProvider.DefaultAccountDataDirectory, "microsoft", "credentials.dat"))
    {
    }

    internal DpapiMicrosoftJsonStorage(string credentialPath)
    {
        this.credentialPath = Path.GetFullPath(credentialPath);
    }

    public JsonNode? ReadAsJsonNode()
    {
        lock (ioLock)
        {
            if (!File.Exists(credentialPath))
                return null;

            try
            {
                var protectedBytes = File.ReadAllBytes(credentialPath);
                var jsonBytes = WindowsDpapiProtector.Unprotect(protectedBytes);
                return JsonNode.Parse(jsonBytes);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or FormatException
                or System.ComponentModel.Win32Exception)
            {
                throw new MicrosoftCredentialStorageException(
                    "Microsoft account credentials could not be read.",
                    exception);
            }
        }
    }

    public void Write(JsonNode node, JsonSerializerOptions? serializerOptions)
    {
        ArgumentNullException.ThrowIfNull(node);
        lock (ioLock)
        {
            var directory = Path.GetDirectoryName(credentialPath)
                ?? throw new InvalidOperationException("Credential path must have a parent directory.");
            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(credentialPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                Directory.CreateDirectory(directory);
                var jsonBytes = Encoding.UTF8.GetBytes(node.ToJsonString(serializerOptions));
                var protectedBytes = WindowsDpapiProtector.Protect(jsonBytes);
                File.WriteAllBytes(temporaryPath, protectedBytes);
                File.Move(temporaryPath, credentialPath, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception)
            {
                throw new MicrosoftCredentialStorageException(
                    "Microsoft account credentials could not be saved.",
                    exception);
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
}
