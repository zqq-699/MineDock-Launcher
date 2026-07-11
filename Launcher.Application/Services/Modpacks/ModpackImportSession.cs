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

using Launcher.Domain.Models;

namespace Launcher.Application.Services;

/// <summary>
/// 保存整合包导入各阶段已取得资源的所有权，以便失败清理只处理真正创建成功的内容。
/// </summary>
internal sealed class ModpackImportSession
{
    public ModpackImportSession(IProgress<LauncherProgress>? progress)
    {
        Progress = progress;
    }

    public IProgress<LauncherProgress>? Progress { get; }

    public PreparedModpack? PreparedModpack { get; set; }

    public StagedModpackInstance? StagedInstance { get; set; }

    public GameInstance? ImportedInstance { get; set; }

    public string? FinalVersionName { get; set; }
}
