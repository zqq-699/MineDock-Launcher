/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

namespace Launcher.Application.Accounts;

public interface IThirdPartyAccountTokenStore
{
    Task<ThirdPartyAccountTokens?> GetAsync(string accountId, CancellationToken cancellationToken = default);

    Task SaveAsync(string accountId, ThirdPartyAccountTokens tokens, CancellationToken cancellationToken = default);

    Task DeleteAsync(string accountId, CancellationToken cancellationToken = default);
}
