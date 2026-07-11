/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Accounts;

public sealed class ThirdPartyAccountLoginException : Exception
{
    public ThirdPartyAccountLoginException(
        ThirdPartyAccountLoginFailureReason reason,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public ThirdPartyAccountLoginFailureReason Reason { get; }
}
