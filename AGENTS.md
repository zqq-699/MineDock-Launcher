# AGENTS.md

本文档记录当前 `launcher` 工作区的项目结构、架构规则、开发约定和自动化代理注意事项。
所有后续开发者、Codex 或自动化代理在修改代码前都必须先阅读并遵守本文件。

\---

## 1\. 项目目标

这是一个基于 **WPF + C# / .NET 8** 的 Minecraft Launcher。

项目目标：

* 遵循 MVVM 架构。
* 分离 UI、业务逻辑、领域模型、数据访问和外部依赖。
* 保持项目结构清晰、可维护、可测试。
* 避免把新功能堆进 `MainViewModel` 或单个巨大 ViewModel。
* 避免用户可见文本散落在 XAML 或 C# 代码中。
* 避免重复 XAML 样式和重复业务逻辑。

\---

## 2\. 当前解决方案结构

当前解决方案包含以下项目：

```text
Launcher.App
Launcher.Application
Launcher.Domain
Launcher.Infrastructure
Launcher.Tests
```

### Launcher.App

WPF 表现层。

负责：

* View
* ViewModel
* Controls
* Behaviors
* Converters
* UI Services
* Resources
* Styles
* Assets
* 页面动画和纯 UI 行为

不负责：

* 文件读写
* 网络请求
* CmlLib 调用
* Modrinth API 调用
* Microsoft Auth 调用
* 复杂业务流程

### Launcher.Application

应用业务层。

负责：

* 业务接口
* UseCase / Application Service
* 账户编排
* 实例业务
* 下载 / Mod / 启动器业务抽象
* Repository 接口

不负责：

* WPF UI
* XAML
* 文件系统具体实现
* HttpClient 具体请求
* CmlLib 具体调用
* Modrinth / Microsoft API 具体实现

### Launcher.Domain

领域层。

负责：

* 纯模型
* 枚举
* 设置状态
* 游戏实例模型
* Loader / Mod / 进度模型
* 与外部 API 无关的业务概念模型

不负责：

* WPF
* 文件路径计算
* Environment / AppData
* JSON 读写
* API DTO
* CmlLib
* HttpClient
* Microsoft Auth
* Modrinth API

### Launcher.Infrastructure

基础设施层。

负责：

* JSON 持久化
* 文件系统
* 路径提供器
* CmlLib
* Minecraft 启动
* Minecraft 版本获取
* Modrinth API
* Microsoft Auth
* Minecraft Profile API
* 本地 Mod 文件操作

### Launcher.Tests

测试项目。

负责：

* Application Service 测试
* ViewModel 测试
* Repository / Infrastructure 测试
* ResourceDictionary 加载测试
* Fake / Test helper

\---

## 3\. 依赖方向

正确依赖方向：

```text
Launcher.App            -> Launcher.Application
Launcher.App            -> Launcher.Domain
Launcher.App            -> Launcher.Infrastructure 仅限 App.xaml.cs 组合根 / DI 注册

Launcher.Application    -> Launcher.Domain

Launcher.Infrastructure -> Launcher.Application
Launcher.Infrastructure -> Launcher.Domain

Launcher.Tests          -> 需要测试的各层
```

禁止：

```text
Launcher.Domain         -> Launcher.Application
Launcher.Domain         -> Launcher.Infrastructure
Launcher.Domain         -> Launcher.App

Launcher.Application    -> Launcher.App
Launcher.Application    -> Launcher.Infrastructure

Launcher.Infrastructure -> Launcher.App
```

`Launcher.App` 可以引用 `Launcher.Infrastructure`，但仅限组合根注册，例如 `App.xaml.cs` 中调用 `AddLauncherInfrastructure()`。
除 DI 注册外，App 层其它类不应直接使用 Infrastructure 的具体实现。

\---

## 4\. Launcher.Core 规则

`Launcher.Core` 是旧结构。

当前有效架构已经改为：

```text
Domain + Application + Infrastructure
```

因此：

* 不要恢复 `Launcher.Core`。
* 不要向 `Launcher.Core` 添加源码。
* 不要添加新的 `using Launcher.Core`。
* 如果目录还存在，应视为迁移残留。
* 如果确认没有引用，可以删除。

\---

## 5\. 当前主要目录职责

### Launcher.Domain

```text
Launcher.Domain/Models
```

当前主要模型：

* `GameInstance`
* `LauncherSettings`
* `LauncherDefaults`
* `LauncherAccountRecord`
* `LauncherCapeRecord`
* `LauncherProgress`
* `LoaderKind`
* `LoaderVersionInfo`
* `MinecraftVersionInfo`
* `LocalMod`
* `ModrinthModels`

注意：

* `LauncherDefaults` 只应保留纯默认常量。
* 路径计算不应放在 Domain。
* Modrinth API DTO 不应放在 Domain。
* Domain 只保留业务概念模型。

### Launcher.Application

```text
Launcher.Application/Services
Launcher.Application/Repositories
Launcher.Application/Accounts
```

负责业务接口和业务编排。

典型内容：

* `ISettingsService`
* `IGameVersionService`
* `IGameInstanceService`
* `GameInstanceService`
* `ILaunchService`
* `ILoaderProvider`
* `IModService`
* `IModrinthService`
* `IGameInstanceRepository`
* `IAccountStore`
* `AccountStore`
* `IMicrosoftAccountService`

Application 层应该通过接口描述业务能力，不关心具体 JSON、HTTP、CmlLib 或文件系统怎么实现。

### Launcher.Infrastructure

```text
Launcher.Infrastructure/Persistence
Launcher.Infrastructure/FileSystem
Launcher.Infrastructure/Minecraft
Launcher.Infrastructure/Modrinth
Launcher.Infrastructure/Accounts
```

负责具体外部实现。

典型内容：

* `LauncherPathProvider`
* `JsonSettingsService`
* `JsonGameInstanceRepository`
* `ModService`
* `GameVersionService`
* `LaunchService`
* `LoaderProviders`
* `VanillaVersionIsolator`
* `DownloadSpeedTrackingGameInstaller`
* `ModrinthService`
* `Modrinth/Dto/ModrinthApiModels`
* `MicrosoftAccountService`
* `MicrosoftAuthProvider`
* `MinecraftProfileClient`
* `MinecraftSkinService`
* `MinecraftCapeService`
* `AccountAvatarService`

### Launcher.App

```text
Launcher.App/Views
Launcher.App/ViewModels
Launcher.App/Controls
Launcher.App/Behaviors
Launcher.App/Services
Launcher.App/Converters
Launcher.App/Animations
Launcher.App/Utilities
Launcher.App/Resources
Launcher.App/Styles
Launcher.App/Assets
```

负责 UI、Binding、动画、控件、样式、文本资源和 UI 服务。

### Launcher.Tests

测试已按功能拆分。

典型测试文件：

* `SettingsServiceTests`
* `GameInstanceServiceTests`
* `ModServiceTests`
* `ModrinthServiceTests`
* `DownloadPageViewModelTests`
* `DownloadTasksPageViewModelTests`
* `AccountStoreTests`
* `AccountPageViewModelTests`
* `ResourceDictionaryTests`

不要重新创建大杂烩式测试文件，例如旧的 `LauncherCoreTests.cs`。

\---

## 6\. Codex / 自动化代理强制规则

后续所有 Codex 修改必须遵守：

1. 不要为了快速实现，把代码塞进最近的 ViewModel。
2. 不要把新业务塞进 `MainViewModel`。
3. 不要把多个功能区塞进一个巨大 ViewModel。
4. 不要在 ViewModel 中直接访问文件系统、网络 API、CmlLib、Microsoft Auth 或 Modrinth API。
5. 不要在 code-behind 中写业务流程。
6. 不要在 XAML 或 C# 中硬编码用户可见文本。
7. 不要复制大量重复 XAML 样式。
8. 不要让 Domain 依赖外部 API DTO、JSON、WPF 或文件路径。
9. 不要让 Application 依赖 WPF 或 Infrastructure 具体实现。
10. 不要恢复 `Launcher.Core`。

\---

## 7\. 新功能开发流程

新增功能时必须按以下顺序设计：

1. 判断是否需要新增 Domain 模型或枚举。
2. 在 Application 层定义业务接口、UseCase 或 Application Service。
3. 在 Infrastructure 层实现文件读写、API 请求、CmlLib 调用或外部依赖。
4. 在 App 层新增 ViewModel、View、Control、Behavior、Style 或 Resource。
5. 在 DI 中注册新服务。
6. 添加或更新测试。
7. 运行：

```powershell
dotnet build Launcher.sln
```

8. 如果涉及业务逻辑，运行：

```powershell
dotnet test Launcher.sln
```

不要从 ViewModel 直接开始写具体实现。

\---

## 8\. ViewModel 规则

ViewModel 只负责：

* 页面状态
* Binding 属性
* ICommand
* 调用 Application 接口
* 协调 UI Services
* 页面内多个子 ViewModel 的组合

ViewModel 不负责：

* 文件读写
* 网络请求
* CmlLib 调用
* Microsoft 登录具体实现
* Modrinth API 具体请求
* JSON 保存
* 路径计算
* 复杂业务流程
* 大量 UI 动画细节

### 大 ViewModel 拆分规则

如果一个 ViewModel 同时负责三类以上业务，应优先拆分。

例如账户页：

```text
AccountPageViewModel
```

只应作为组合门面，协调：

```text
AccountListViewModel
AccountDialogViewModel
AccountAppearanceViewModel
```

例如游戏管理页：

```text
GameManagementViewModel
```

只应作为组合门面，协调：

```text
InstanceManagementViewModel
LoaderSelectionViewModel
LocalModsViewModel
ModrinthSearchViewModel
```

如果新增账户、皮肤、披风、实例、Mod、Loader 等功能，应优先放入对应子 ViewModel 或 Application Service，不要塞回大 ViewModel。

\---

## 9\. MainViewModel 限制

`MainViewModel` 只能负责 Shell 级别状态：

* 当前页面
* 导航命令
* 全局状态栏
* 全局弹窗宿主
* 窗口命令
* 子页面 ViewModel 的组合与切换
* 导航菜单选中状态

`MainViewModel` 禁止负责：

* Minecraft 版本下载
* 游戏启动细节
* 实例创建细节
* 实例保存细节
* Mod 搜索
* Mod 安装
* Mod 启停
* Microsoft 登录
* 皮肤上传
* 披风应用
* 改名 API
* JSON 文件读写
* CmlLib 调用
* Modrinth API 调用
* 文件系统操作

新增功能时，如果想把代码写进 `MainViewModel`，必须先判断是否应该新建子 ViewModel、UseCase 或 Service。

\---

## 10\. View / code-behind 规则

View 和 `\*.xaml.cs` 只允许包含：

* `InitializeComponent`
* 纯 UI 初始化
* 视觉树查找
* 动画触发
* 滚动定位
* 控件焦点处理
* 无法通过 Binding 表达的纯视图行为

View 和 `\*.xaml.cs` 禁止包含：

* 账户登录流程
* 下载逻辑
* 实例创建
* Mod 管理
* 设置保存
* 文件读写
* API 请求
* CmlLib 调用
* 业务判断
* 业务状态修改

按钮点击、选择状态、页面切换、确认操作，优先使用：

```text
Binding + ICommand
```

\---

## 11\. UI 文本规则

所有用户可见文本必须放入：

```text
Launcher.App/Resources/Strings.resx
```

并通过：

```text
Launcher.App/Resources/Strings.cs
```

访问。

用户可见文本包括：

* 页面标题
* 菜单文字
* 按钮文字
* 弹窗标题
* 弹窗内容
* 状态栏提示
* 错误提示
* 空状态提示
* 确认 / 取消 / 删除 / 重命名等操作文字

禁止在 XAML 或 C# 中直接硬编码用户可见文本，例如：

```csharp
\_statusService.Report("启动失败");
```

应使用：

```csharp
\_statusService.Report(Strings.Status\_LaunchFailed);
```

XAML 中应使用资源引用，例如：

```xml
Text="{x:Static res:Strings.Page\_Home}"
```

允许保留在代码中的字符串：

* 内部日志 key
* API 参数
* 文件名
* 协议字段
* 不直接展示给用户的技术异常信息
* 测试数据
* 调试信息

底层 exception message 不应直接展示给用户。
用户看到的错误应转换为 `Strings.resx` 中的友好文案。

\---

## 12\. 新代码放置规则

新增文件时按职责放置：

```text
页面 XAML
-> Launcher.App/Views

页面状态、命令、Binding 属性
-> Launcher.App/ViewModels

可复用控件
-> Launcher.App/Controls

Attached Behavior、滚动行为、UI 动画行为
-> Launcher.App/Behaviors

自定义 Animation
-> Launcher.App/Animations

视觉树查找、纯 UI 工具方法
-> Launcher.App/Utilities

颜色、字体、间距、圆角、阴影
-> Launcher.App/Resources

控件样式
-> Launcher.App/Styles

用户可见文本
-> Launcher.App/Resources/Strings.resx

业务接口
-> Launcher.Application/Services

UseCase / 业务流程
-> Launcher.Application

仓储接口
-> Launcher.Application/Repositories

账户业务编排
-> Launcher.Application/Accounts

纯模型、枚举、领域状态
-> Launcher.Domain/Models

JSON 读写
-> Launcher.Infrastructure/Persistence

路径计算
-> Launcher.Infrastructure/LauncherPathProvider 或 Infrastructure 相关目录

CmlLib 实现
-> Launcher.Infrastructure/Minecraft

Microsoft 登录 / Profile API
-> Launcher.Infrastructure/Accounts

Modrinth API DTO / Client / 实现
-> Launcher.Infrastructure/Modrinth

本地文件系统操作
-> Launcher.Infrastructure/FileSystem

测试
-> Launcher.Tests
```

不确定文件应该放哪里时，先判断职责，不要直接创建。

\---

## 13\. 可拆分优先原则

出现以下情况时，优先拆分：

* 一个类超过 300 行并继续增长。
* 一个 ViewModel 管理多个功能区。
* 一个 Service 同时处理多种外部 API。
* 一个方法超过 80 行。
* 一个类依赖太多服务。
* 一个类既处理 UI 状态又处理业务流程。
* 类名变成模糊的 `Manager`、`Helper`、`Controller`，但职责不清。
* 为了新增功能需要频繁修改同一个大类。

推荐拆分方式：

```text
页面组合 VM
-> 只负责组合和协调

子 ViewModel
-> 负责具体页面区域状态

Application Service / UseCase
-> 负责业务流程

Infrastructure Service
-> 负责具体外部实现
```

不要为了“少文件”把所有逻辑堆进一个大类。

\---

## 14\. 样式和控件复用规则

不要在页面 XAML 中复制大量重复样式。

以下内容应优先抽到 ResourceDictionary、Style、ControlTemplate、UserControl 或 CustomControl：

* 按钮样式
* 列表项样式
* 卡片样式
* 弹窗样式
* 输入框样式
* 滚动条样式
* 页面标题样式
* 圆角
* 阴影
* 间距
* 字体
* 颜色
* 重复出现的布局结构

当前样式目录：

```text
Launcher.App/Resources/ThemeResources.xaml
Launcher.App/Styles/ControlStyles.xaml
Launcher.App/Styles/ControlStyles.Navigation.xaml
Launcher.App/Styles/ControlStyles.Page.xaml
Launcher.App/Styles/ControlStyles.Scroll.xaml
Launcher.App/Styles/ControlStyles.Lists.xaml
Launcher.App/Styles/ControlStyles.Buttons.xaml
Launcher.App/Styles/ControlStyles.Dialogs.xaml
Launcher.App/Styles/ControlStyles.Inputs.xaml
```

新增样式前，先检查是否已有可复用样式。

\---

## 15\. WPF 长列表性能规则

涉及长列表时，例如：

* 版本列表
* Mod 列表
* 实例列表
* 账户列表
* 搜索结果列表

必须优先使用 WPF 原生虚拟化。

要求：

* 使用 `ListBox` / `ListView` + `VirtualizingStackPanel`。
* 开启 `VirtualizingPanel.IsVirtualizing="True"`。
* 开启 `VirtualizingPanel.VirtualizationMode="Recycling"`。
* 不要在虚拟化列表外层套普通 `ScrollViewer`。
* 不要手写虚拟列表，除非有明确必要。
* 不要在滚动时频繁 Clear / Add / Remove 整个集合。
* 平滑滚动只能控制 ScrollViewer offset，不应触发集合重建。
* 列表项模板避免过多阴影、模糊、大图片和复杂视觉树。
* 图片应尽量加载缩略图并缓存。

下载版本列表必须保持：

```text
WPF 原生 ListBox + VirtualizingStackPanel + Recycling
```

不要恢复旧的手写虚拟列表。

\---

## 16\. 下载页规则

下载页已经有明确结构：

```text
DownloadPageViewModel
DownloadVersionFilter
DownloadInstanceNameTracker
DownloadInstallProgress
DownloadTasksPageViewModel
DownloadVersionListView
DownloadInstanceOptionsView
```

要求：

* 版本筛选逻辑放在 `DownloadVersionFilter` 或相关 Application 逻辑中。
* 实例名重复判断放在 `DownloadInstanceNameTracker`。
* 安装进度适配放在 `DownloadInstallProgress`。
* 下载任务状态放在 `DownloadTasksPageViewModel`。
* 下载列表 UI 行为可以留在 `DownloadVersionListView.xaml.cs`，但不能手写虚拟窗口。
* 页面切换动画和滚动定位应保持为纯 UI 行为。

\---

## 17\. 账户页规则

账户页已经拆成：

```text
AccountPageViewModel
AccountListViewModel
AccountDialogViewModel
AccountAppearanceViewModel
```

要求：

* `AccountPageViewModel` 只作为组合门面。
* 账户列表、选择、默认账户放在 `AccountListViewModel`。
* 添加、删除、重命名、Microsoft 登录弹窗流程放在 `AccountDialogViewModel`。
* 皮肤、披风、资料刷新放在 `AccountAppearanceViewModel`。
* 账户持久化和账户顺序保存通过 `IAccountStore`。
* Microsoft 登录、皮肤、披风、改名通过 `IMicrosoftAccountService`。
* 不要把账户新功能重新塞回 `AccountPageViewModel`。

\---

## 18\. 游戏管理页规则

游戏管理页已经拆成：

```text
GameManagementViewModel
InstanceManagementViewModel
LoaderSelectionViewModel
LocalModsViewModel
ModrinthSearchViewModel
```

要求：

* `GameManagementViewModel` 只作为组合门面。
* 实例列表、创建、保存、默认实例放在 `InstanceManagementViewModel`。
* Minecraft 版本和 Loader 选择放在 `LoaderSelectionViewModel`。
* 本地 Mod 导入、启停、删除、刷新放在 `LocalModsViewModel`。
* Modrinth 搜索和安装放在 `ModrinthSearchViewModel`。
* 实例业务下沉到 `IGameInstanceService` 或 Application UseCase。
* Mod 文件操作通过 `IModService`。
* Modrinth 安装通过 `IModrinthService`。
* 不要把新游戏管理功能重新塞回 `GameManagementViewModel`。

\---

## 19\. Infrastructure 规则

Infrastructure 可以依赖外部库和系统 API。

允许：

* `HttpClient`
* CmlLib
* Microsoft Auth
* Minecraft Profile API
* Modrinth API
* JSON
* 文件系统
* 路径计算
* 图片下载和缓存

要求：

* 对外暴露 Application 层接口。
* 外部 API DTO 放在 Infrastructure 内部目录，例如 `Modrinth/Dto`。
* 不要让 App 直接依赖 Infrastructure 小服务。
* 如果一个 Infrastructure Service 太大，应拆成多个内部服务，再用 facade 对外提供接口。

例如 Microsoft 账户：

```text
MicrosoftAccountService
-> facade

MicrosoftAuthProvider
-> 登录和 token

MinecraftProfileClient
-> Profile API

MinecraftSkinService
-> 皮肤上传

MinecraftCapeService
-> 披风读取和应用

AccountAvatarService
-> 头像下载、缓存、生成
```

\---

## 20\. 测试规则

新增业务逻辑时，应补充测试。

优先测试：

* Application Service / UseCase
* ViewModel 命令和状态
* Repository / Infrastructure 行为
* ResourceDictionary 加载
* 长列表筛选和任务状态
* 边界条件

测试禁止依赖：

* 真实 Microsoft 登录
* 真实大型用户目录
* 不稳定网络
* 用户本地真实数据

应使用：

* fake service
* mock
* 临时目录
* 测试专用数据
* 捕获用 handler

测试文件应按功能拆分，不要重新写一个大杂烩测试文件。

\---

## 21\. 编码和文本规则

* 所有 `.cs`、`.xaml`、`.resx`、`.json`、`.md` 文件应使用 UTF-8。
* 修改中文文案后必须检查是否乱码。
* 新增用户可见文本必须写入 `Strings.resx`。
* 不要混用多套中文 / 英文硬编码提示。
* 不要把底层技术异常直接展示给用户。
* `Strings.cs` 是手写资源包装类。
* 不要把手写资源包装类命名为 `Strings.Designer.cs`，避免和自动生成文件混淆。

\---

## 22\. 构建与测试命令

常用命令：

```powershell
dotnet build Launcher.sln
dotnet test Launcher.sln
```

修改 XAML、资源字典、样式后，至少运行：

```powershell
dotnet build Launcher.sln
```

修改业务逻辑、ViewModel、Service、Repository 后，运行：

```powershell
dotnet test Launcher.sln
```

如果 WPF 构建在受限环境出现 SDK 路径权限问题，需要在本机正常 PowerShell 环境中重新运行。

\---

## 23\. 修改前检查清单

开始写代码前，先判断：

* 这个功能属于 UI、业务、领域模型，还是外部实现？
* 是否需要新增接口？
* 是否需要新增 UseCase 或 Service？
* 是否会让 `MainViewModel` 变重？
* 是否会让某个页面 ViewModel 继续膨胀？
* 是否有重复 UI 可以抽成控件或样式？
* 是否有用户可见文本需要放进 `Strings.resx`？
* 是否需要新增测试？
* 是否会破坏现有绑定、动画或 UI？
* 是否会破坏依赖方向？

\---

## 24\. 修改后检查清单

完成修改后，必须检查：

* 是否仍然符合 MVVM？
* ViewModel 是否没有直接依赖 Infrastructure 具体实现？
* code-behind 是否没有业务逻辑？
* 新增 UI 文本是否进入 `Strings.resx`？
* 是否没有恢复 `Launcher.Core`？
* 是否没有把 API DTO 放进 Domain？
* 是否没有复制大量重复 XAML？
* 是否保持现有 UI 和动画？
* 是否需要更新 AGENTS.md？
* 是否运行 `dotnet build Launcher.sln`？
* 涉及业务时是否运行 `dotnet test Launcher.sln`？

\---

## 25\. 禁止事项

禁止：

* 向 `Launcher.Core` 添加任何源码。
* 把新功能直接塞进 `MainViewModel`。
* 把多个页面的业务塞进一个 ViewModel。
* 让 `AccountPageViewModel` 重新变成账户功能垃圾桶。
* 让 `GameManagementViewModel` 重新变成游戏管理垃圾桶。
* 在 ViewModel 中 new Infrastructure 服务。
* 在 ViewModel 中直接读写文件。
* 在 ViewModel 中直接请求网络 API。
* 在 ViewModel 中直接调用 CmlLib。
* 在 ViewModel 中直接调用 Microsoft Auth。
* 在 ViewModel 中直接调用 Modrinth API。
* 在 code-behind 中写业务流程。
* 在 XAML 或 C# 中硬编码用户可见文本。
* 把外部 API Response DTO 长期放在 Domain。
* 把 WPF 类型引入 Domain 或 Application。
* 为了快速实现而破坏依赖方向。
* 无必要大规模重命名公共类、绑定属性或资源 key。
* 删除现有功能或破坏现有 UI 动画。
* 恢复旧的手写虚拟列表。
* 把重复样式继续复制到页面 XAML。

\---

## 26\. 总原则

在每次回答前说一声“喵”

写代码时始终遵守：

```text
UI 归 UI
业务归 Application
模型归 Domain
外部实现归 Infrastructure
测试归 Tests
文本归 Strings.resx
样式归 ResourceDictionary
长逻辑要拆
大 ViewModel 要拆
MainViewModel 不准变垃圾桶
```

