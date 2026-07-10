/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests.Fakes;

public sealed class FakeAccountSkinLibraryService : IAccountSkinLibraryService
{
    public Func<LauncherAccount, string, MinecraftSkinModel, LauncherSkinRecord>? ImportSkinHandler { get; init; }

    public Func<LauncherAccount, IReadOnlyList<LauncherSkinRecord>>? GetAvailableSkinsHandler { get; init; }

    public Func<LauncherAccount, LauncherSkinRecord, Task>? DeleteSkinHandler { get; init; }

    public int ImportSkinCount { get; private set; }

    public int DeleteSkinCount { get; private set; }

    public string? LastSkinFilePath { get; private set; }

    public MinecraftSkinModel? LastSkinModel { get; private set; }

    public LauncherSkinRecord? LastDeletedSkin { get; private set; }

    public IReadOnlyList<LauncherSkinRecord> GetAvailableSkins(LauncherAccount account)
    {
        return GetAvailableSkinsHandler?.Invoke(account) ?? account.SkinLibrary;
    }

    public Task<LauncherSkinRecord> ImportSkinAsync(
        LauncherAccount account,
        string skinFilePath,
        MinecraftSkinModel skinModel,
        CancellationToken cancellationToken = default)
    {
        ImportSkinCount++;
        LastSkinFilePath = skinFilePath;
        LastSkinModel = skinModel;
        return Task.FromResult(ImportSkinHandler is null
            ? new LauncherSkinRecord
            {
                Id = $"skin-{ImportSkinCount}",
                Source = skinFilePath,
                SkinModel = skinModel,
                ContentHash = $"hash-{ImportSkinCount}",
                AddedAtUtc = DateTimeOffset.UtcNow
            }
            : ImportSkinHandler(account, skinFilePath, skinModel));
    }

    public Task DeleteSkinAsync(
        LauncherAccount account,
        LauncherSkinRecord skin,
        CancellationToken cancellationToken = default)
    {
        DeleteSkinCount++;
        LastDeletedSkin = skin;
        return DeleteSkinHandler?.Invoke(account, skin) ?? Task.CompletedTask;
    }
}
