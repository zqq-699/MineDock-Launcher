# BlockHelm-Launcher

[中文](#chinese) | [English](#english)

<a id="chinese"></a>

## 中文

### 简介

`BlockHelm-Launcher` 是一个 Minecraft 启动器，目标是把日常启动、实例管理、资源安装和账户管理做得更清楚、更顺手。

这是一个 **纯 AI 编写、设计并持续迭代** 的项目。人工主要负责提出需求、判断体验、验收结果和决定下一步方向；代码、界面、文案和功能实现都由 AI 完成。

它目前仍在开发中，但已经围绕普通玩家常用的启动器流程做了不少能力：选择游戏、安装版本、管理 Mod 和资源、配置 Java 与内存、处理启动失败，以及切换深色 / 浅色主题。

### 功能概览

#### 启动与实例

- 从首页选择已有游戏实例并启动。
- 显示启动进度和当前状态。
- 启动失败时给出反馈，并记录诊断信息。
- 支持实例创建、重命名、删除和独立设置。
- 支持实例级 Java、内存、启动参数、启动前命令和退出后命令。
- 支持实例备份、恢复、删除和备份目录管理。

#### 账户与外观

- 支持离线账户。
- 支持 Microsoft 账户登录。
- 支持账户列表、当前账户切换、重命名和删除。
- 支持头像、皮肤、披风相关管理能力。
- 支持离线账户 UUID 生成方式设置。

#### 版本与加载器

- 支持安装原版 `Vanilla` 游戏版本。
- 支持安装 `Fabric` 和 `Forge`。
- `NeoForge` 和 `Quilt` 已有入口与相关能力铺垫，完整体验仍在继续完善。
- 支持版本列表、版本筛选、实例命名和重复名称提示。
- 支持下载任务状态展示。

#### 资源管理

- 支持管理实例内的 Mod、存档、资源包和光影包。
- 支持本地导入 Mod、存档压缩包、资源包和光影包。
- 支持打开对应资源文件夹。
- 支持多选、全选、删除等批量操作。
- 支持本地整合包压缩包导入，并在部分文件需要手动补全时给出提示。

#### 在线资源

- 支持在线搜索 Mod、资源包、光影包、世界和整合包。
- 支持按 Minecraft 版本、加载器、来源和分类筛选。
- 支持查看资源详情、可用版本和依赖信息。
- 支持安装兼容资源到指定游戏实例，或下载到本地目录。
- 支持 Modrinth；CurseForge 相关结果会根据配置情况展示。
- 支持前置 Mod 检查，并可辅助安装缺少的前置依赖。

#### 设置与体验

- 支持自动发现 Java，也可以手动导入或选择 Java。
- 支持全局内存设置和实例独立内存设置。
- 支持游戏下载源选择、下载限速和 Minecraft 目录设置。
- 支持深色主题、浅色主题、跟随系统和运行时切换。
- 支持多种强调色。
- 支持启动器日志目录、诊断日志和错误反馈。
- 支持检查启动器更新，并在可用时尝试自动更新。

### 当前状态

这个项目还在持续开发中。已经可用的功能会继续打磨，未完成的入口会逐步补齐。README 会尽量保持和当前实现一致，避免把计划中的功能写成已经完成。

### 运行方式

环境要求：

- Windows 10 / 11
- .NET 8 SDK

本地运行：

```powershell
dotnet run --project Launcher.App\Launcher.App.csproj
```

也可以直接使用：

```powershell
.\RunLauncher.bat
```

如果你是开发者，具体开发约定请查看 [AGENTS.md](./AGENTS.md)。

---

<a id="english"></a>

## English

### Overview

`BlockHelm-Launcher` is a Minecraft launcher focused on everyday launching, instance management, resource installation, account management, and a clean user experience.

This project is **written, designed, and continuously iterated entirely by AI**. Human involvement is mainly about giving product direction, reviewing the experience, accepting results, and deciding what should be improved next.

It is still under active development, but it already covers many common launcher workflows: launching games, installing versions, managing resources, configuring Java and memory, diagnosing launch failures, and switching between dark and light themes.

### Features

#### Launching and Instances

- Select and launch existing game instances from the home page.
- Show launch progress and current launch status.
- Provide failure feedback and diagnostic logs when launching fails.
- Create, rename, delete, and configure instances independently.
- Configure instance-level Java, memory, launch arguments, pre-launch commands, and post-exit commands.
- Create, restore, delete, and manage instance backups.

#### Accounts and Appearance

- Offline account support.
- Microsoft account support.
- Account list, active account switching, rename, and delete actions.
- Avatar, skin, and cape related management.
- Offline account UUID generation options.

#### Versions and Loaders

- Install `Vanilla` Minecraft versions.
- Install `Fabric` and `Forge`.
- `NeoForge` and `Quilt` have entry points and groundwork in place, with the full experience still being refined.
- Version lists, filters, instance naming, and duplicate-name feedback.
- Download task status display.

#### Resource Management

- Manage instance mods, saves, resource packs, and shader packs.
- Import local mods, save archives, resource packs, and shader packs.
- Open resource folders directly.
- Multi-select, select-all, delete, and other batch actions.
- Import local modpack archives, with guidance when some files need to be retried or completed manually.

#### Online Resources

- Search online mods, resource packs, shader packs, worlds, and modpacks.
- Filter by Minecraft version, loader, source, and category.
- View project details, available versions, and dependency information.
- Install compatible resources into a selected game instance, or download them locally.
- Modrinth is supported; CurseForge results depend on configuration.
- Check required mod dependencies and help install missing dependencies.

#### Settings and Experience

- Discover Java automatically, or import and select Java manually.
- Configure global memory and per-instance memory.
- Choose download sources, speed limits, and the Minecraft directory.
- Use dark mode, light mode, follow-system behavior, and runtime theme switching.
- Choose from multiple accent colors.
- Open launcher log directories and use diagnostic logs for troubleshooting.
- Check for launcher updates and attempt automatic updates when available.

### Status

BlockHelm-Launcher is still under active development. Existing features will continue to be polished, and unfinished entry points will be completed over time. This README aims to describe what is currently present without presenting planned work as finished.

### Run

Requirements:

- Windows 10 / 11
- .NET 8 SDK

Run locally:

```powershell
dotnet run --project Launcher.App\Launcher.App.csproj
```

Or use:

```powershell
.\RunLauncher.bat
```

For repository-specific development rules, see [AGENTS.md](./AGENTS.md).
