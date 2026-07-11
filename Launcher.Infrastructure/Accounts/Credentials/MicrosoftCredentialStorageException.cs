/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Infrastructure.Accounts.Credentials;

internal sealed class MicrosoftCredentialStorageException : Exception
{
    public MicrosoftCredentialStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
