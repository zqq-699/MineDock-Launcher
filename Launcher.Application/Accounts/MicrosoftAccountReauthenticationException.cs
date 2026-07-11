/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Accounts;

public sealed class MicrosoftAccountReauthenticationException : Exception
{
    public MicrosoftAccountReauthenticationException(
        MicrosoftAccountReauthenticationFailureReason reason,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public MicrosoftAccountReauthenticationFailureReason Reason { get; }
}
