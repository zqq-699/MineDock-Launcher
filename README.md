# Launcher

[中文](#chinese) | [English](#english)

<a id="chinese"></a>

## 中文

### 项目简介

`Launcher` 是一个基于 **WPF + C# / .NET 8** 的 Minecraft Launcher，目标是做成一个结构清晰、可维护、易扩展的桌面启动器。

这个仓库强调：

- 严格按 **MVVM** 分层
- UI / 业务 / 领域 / 基础设施职责分离
- 用户文本资源化
- 主题颜色统一走资源字典
- 深色 / 浅色主题都可运行时切换
- 关键流程必须接入日志和测试

### 当前功能

- 账户管理：支持离线账户与 Microsoft 账户
- 外观能力：账户头像、皮肤、披风管理
- 版本安装：支持 `Vanilla`、`Fabric`、`Forge`
- 预留加载器：`NeoForge`、`Quilt` 目前为占位，尚未实现完整安装流程
- 游戏启动：带进度、失败诊断、异常退出分析
- 实例管理：支持实例创建、重命名、删除、设置隔离
- 资源管理：支持 Mod、存档、资源包、光影包的本地管理
- Modrinth 集成：搜索并安装兼容 Mod
- 整包导入：支持本地 Modpack 压缩包导入
- 全局设置：Java 自动发现 / 选择、内存分配、下载源、限速、主题、强调色、日志目录
- UI 主题：深色 / 浅色、跟随系统、运行时切换强调色
- 日志系统：基于 Serilog，包含启动器日志与启动诊断

### 技术栈

- .NET 8
- WPF
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
- Serilog
- CmlLib.Core

### 解决方案结构

```text
Launcher.App            WPF UI、ViewModel、控件、主题、资源、UI 服务
Launcher.Application    应用层接口与业务编排
Launcher.Domain         纯领域模型、枚举、状态对象
Launcher.Infrastructure 文件系统、持久化、Minecraft、认证、Modrinth 等外部实现
Launcher.Tests          单元测试与资源/主题测试
```

### 快速开始

#### 环境要求

- Windows 10 / 11
- .NET 8 SDK

#### 本地运行

```powershell
dotnet run --project Launcher.App\Launcher.App.csproj
```

或直接使用：

```powershell
.\RunLauncher.bat
```

#### 构建

```powershell
dotnet build Launcher.sln
```

#### 测试

```powershell
dotnet test Launcher.sln
```

#### 发布

```powershell
.\BuildSingleRelease.bat
```

默认发布输出目录：

```text
publish\Launcher-win-x64-fdd-single
```

### 数据目录与日志

- 启动器数据目录：`%APPDATA%\Launcher`
- 日志目录：`%APPDATA%\Launcher\log`
- 默认游戏目录：程序目录下的 `.minecraft`

如需清理本地启动器数据，可使用：

```powershell
.\ClearLauncherAppData.bat
```

### 开发约定

开始修改前建议先阅读 [AGENTS.md](./AGENTS.md)。

核心约定包括：

- 不把复杂功能塞进 `MainViewModel`
- ViewModel 不直接访问文件、网络、CmlLib 或外部 API
- 所有用户可见文本统一进入 `Launcher.App/Resources/Strings.resx`
- 所有主题色统一进入 `Launcher.App/Resources/Themes`
- 新功能必须补日志
- 涉及业务逻辑时必须补测试
- 保持依赖方向清晰，避免跨层倒灌

### 开发状态

仓库仍在持续迭代中，部分页面和能力可能还会继续拆分、补全或优化。README 会尽量跟随当前代码更新，但具体实现请以源码为准。

---

<a id="english"></a>

## English

### Overview

`Launcher` is a **WPF + C# / .NET 8** Minecraft launcher focused on clean architecture, maintainable MVVM, and theme-safe desktop UI.

The project is built around:

- clear separation of UI, application logic, domain models, and infrastructure
- resource-based user-facing text
- centralized theme resources
- runtime switching for light and dark themes
- logging and test coverage for important flows

### Current Features

- Account management for offline and Microsoft accounts
- Avatar, skin, and cape related account features
- Version installation for `Vanilla`, `Fabric`, and `Forge`
- Reserved placeholders for `NeoForge` and `Quilt` (not fully implemented yet)
- Game launch flow with progress reporting and failure diagnostics
- Instance creation, rename, deletion, and isolated settings management
- Local management for mods, saves, resource packs, and shader packs
- Modrinth search and compatible mod installation
- Local modpack archive import
- Global settings for Java discovery/selection, memory, download source, speed limits, themes, accent colors, and log folders
- Runtime theme switching with dark/light mode and system-follow support
- Serilog-based launcher logging and launch diagnostics

### Tech Stack

- .NET 8
- WPF
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
- Serilog
- CmlLib.Core

### Solution Layout

```text
Launcher.App            WPF UI, ViewModels, controls, themes, resources, UI services
Launcher.Application    application services and use-case orchestration
Launcher.Domain         pure models, enums, and domain state
Launcher.Infrastructure filesystem, persistence, Minecraft integration, auth, Modrinth, external services
Launcher.Tests          unit tests plus resource/theme coverage
```

### Getting Started

#### Requirements

- Windows 10 / 11
- .NET 8 SDK

#### Run locally

```powershell
dotnet run --project Launcher.App\Launcher.App.csproj
```

Or use:

```powershell
.\RunLauncher.bat
```

#### Build

```powershell
dotnet build Launcher.sln
```

#### Test

```powershell
dotnet test Launcher.sln
```

#### Publish

```powershell
.\BuildSingleRelease.bat
```

Default publish output:

```text
publish\Launcher-win-x64-fdd-single
```

### Data and Logs

- Launcher data directory: `%APPDATA%\Launcher`
- Log directory: `%APPDATA%\Launcher\log`
- Default game directory: `.minecraft` beside the application

To clear local launcher data:

```powershell
.\ClearLauncherAppData.bat
```

### Development Notes

Read [AGENTS.md](./AGENTS.md) before making structural changes.

Important repository rules:

- keep `MainViewModel` focused on shell/navigation responsibilities
- do not let ViewModels access filesystem, network, CmlLib, or external APIs directly
- put all user-visible text in `Launcher.App/Resources/Strings.resx`
- keep theme colors in `Launcher.App/Resources/Themes`
- add logging for new features
- add tests for business logic changes
- preserve the intended dependency direction between layers

### Status

This repository is still actively evolving. Some pages and capabilities are still being refined, split, or completed. The README aims to reflect the current codebase, but the source is the ultimate reference.
