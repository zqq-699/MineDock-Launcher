/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Accounts;

public enum LaunchAccountSessionFailureReason
{
    Unknown,
    ReauthenticationRequired,
    AuthenticationServerUnavailable,
    InvalidAuthenticationResponse,
    CredentialStorageFailed
}
