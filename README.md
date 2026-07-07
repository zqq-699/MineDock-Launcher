# MineDock Launcher

[中文](#chinese) | [English](#english)

<a id="chinese"></a>

## 中文

### 简介

`MineDock Launcher` 是一个基于 **WPF + C# / .NET 8** 的 Minecraft 启动器项目。

这个项目更关注“好用的日常启动体验”与“清晰、稳定、可持续演进的产品能力”，而不是只做一个能启动游戏的壳。

### 项目目标

- 提供清晰、流畅的 Minecraft 启动与实例管理体验
- 同时支持普通玩家常用的版本、加载器、资源管理与账户能力
- 做好深色 / 浅色主题、设置管理、日志与诊断这类长期使用体验
- 在持续迭代中逐步补全更完整的整合包、资源管理和启动诊断能力

### 当前功能

- 账户管理
  支持离线账户与 Microsoft 账户
- 账户外观
  支持头像、皮肤、披风相关管理能力
- 版本安装
  支持 `Vanilla`、`Fabric`、`Forge`
- 加载器预留
  `NeoForge`、`Quilt` 已预留入口，但当前仍未完成完整安装实现
- 游戏启动
  支持启动进度、启动失败反馈、异常退出分析与诊断
- 实例管理
  支持实例创建、重命名、删除和独立设置
- 资源管理
  支持 Mod、存档、资源包、光影包的本地管理
- Modrinth 集成
  支持搜索并安装兼容 Mod
- 本地整合包导入
  支持本地 Modpack 压缩包导入
- 全局设置
  支持 Java 发现与选择、内存设置、下载源、限速、主题、强调色等
- 主题系统
  支持深色 / 浅色、跟随系统、运行时切换

### 适合关注什么

如果你关心的是这个项目“现在能不能用、在往哪里做”，重点可以看：

- 是否覆盖常见 Minecraft 启动需求
- 是否能更舒服地管理实例和资源
- 是否能逐步补齐整合包、诊断和账户体验

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

### 当前状态

项目仍在持续开发中。部分能力已经可用，部分能力还在继续补全和打磨，README 会尽量跟随当前实现更新。

如果你是开发者，具体开发约定请查看 [AGENTS.md](./AGENTS.md)。

---

<a id="english"></a>

## English

### Overview

`MineDock Launcher` is a **WPF + C# / .NET 8** Minecraft launcher project.

The goal is not just to launch the game, but to build a launcher that feels solid for everyday use and can keep growing into a more complete product over time.

### Project Goals

- Provide a smooth and clear Minecraft launch and instance management experience
- Cover the features players commonly need around versions, loaders, resources, and accounts
- Treat themes, settings, logs, and diagnostics as first-class long-term usability features
- Gradually expand modpack, resource management, and launch diagnostic capabilities

### Current Features

- Account management
  Supports offline accounts and Microsoft accounts
- Account appearance
  Includes avatar, skin, and cape related features
- Version installation
  Supports `Vanilla`, `Fabric`, and `Forge`
- Loader placeholders
  `NeoForge` and `Quilt` have reserved entry points but are not fully implemented yet
- Game launching
  Includes progress reporting, failure feedback, abnormal-exit analysis, and diagnostics
- Instance management
  Supports instance creation, rename, deletion, and isolated settings
- Resource management
  Supports local mods, saves, resource packs, and shader packs
- Modrinth integration
  Search and install compatible mods
- Local modpack import
  Import local modpack archives
- Global settings
  Java discovery and selection, memory settings, download source, speed limits, theme, accent color, and more
- Theme system
  Dark mode, light mode, follow-system behavior, and runtime switching

### What This README Focuses On

This README is intentionally centered on what the launcher is for and what it can currently do.

If you mainly want to evaluate the project, the key questions are:

- does it already cover common Minecraft launcher workflows?
- does it make instance and resource management easier?
- is it moving toward a fuller launcher experience over time?

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

### Status

The project is still under active development. Several features are already working, while others are still being refined or completed. This README aims to stay aligned with the current implementation.

If you are contributing as a developer, see [AGENTS.md](./AGENTS.md) for repository-specific rules.
