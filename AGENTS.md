# 项目备忘录

本文档记录当前 `launcher` 工作区的项目结构、架构约定、关键文件和维护注意事项，供后续开发者或自动化代理快速接手。

## 项目概览

这是一个基于 WPF + C# / .NET 8 的 Minecraft Launcher 项目，目标是遵循 MVVM，并将 UI、业务逻辑、数据访问和外部依赖分离。

当前解决方案包含以下项目：

- `Launcher.App`：WPF 表现层，包含 View、ViewModel、控件、样式、转换器、UI 服务和资源。
- `Launcher.Domain`：领域模型层，包含启动器设置、实例、版本、Loader、Mod、进度等模型。
- `Launcher.Application`：应用业务层，包含业务接口、用例/服务、账户编排、仓储接口。
- `Launcher.Infrastructure`：基础设施层，包含 JSON 持久化、文件系统、CmlLib、Modrinth API、Microsoft Auth 等具体实现。
- `Launcher.Tests`：xUnit 测试项目。

`Launcher.Core` 已从解决方案中移除，当前结构以 `Domain / Application / Infrastructure` 替代旧的 Core 职责。若文件夹仍存在，多数是迁移残留或构建产物，不应继续向其中添加源码。

## 依赖方向

理想依赖方向如下：

```text
Launcher.App            -> Launcher.Application
Launcher.App            -> Launcher.Domain
Launcher.App            -> Launcher.Infrastructure 仅用于组合根/DI 注册
Launcher.Application    -> Launcher.Domain
Launcher.Infrastructure -> Launcher.Application
Launcher.Infrastructure -> Launcher.Domain
Launcher.Tests          -> 需要测试的各层
```

重要约定：

- ViewModel 不应直接依赖 CmlLib、HttpClient、文件系统、Modrinth API、Microsoft Auth 等具体实现。
- 外部依赖实现应放在 `Launcher.Infrastructure`。
- 业务接口应放在 `Launcher.Application`。
- 纯数据结构和领域状态应放在 `Launcher.Domain`。
- `Launcher.App` 中除 `App.xaml.cs` 组合根外，应尽量避免直接引用 Infrastructure 类型。

## 启动入口与 DI

WPF 启动入口：

- `Launcher.App/App.xaml`
- `Launcher.App/App.xaml.cs`

`App.xaml.cs` 负责创建 `ServiceCollection`，调用：

- `AddLauncherApplication()`
- `AddLauncherInfrastructure()`

并注册 App 层 UI 服务和 ViewModel：

- `IStatusService`
- `IWindowService`
- `IClipboardService`
- `IFilePickerService`
- `IAccountDialogService`
- `AccountPageViewModel`
- `DownloadPageViewModel`
- `GameManagementViewModel`
- `MainViewModel`
- `MainWindow`

## App 层结构

### Views

主要视图位于 `Launcher.App/Views`：

- `HomePageView`
- `AccountPageView`
- `DownloadPageView`
- `DownloadVersionListView`
- `GeneralPageView`
- `AddAccountDialogView`
- `DeleteAccountDialogView`
- `RenameAccountDialogView`

当前 View 基本通过 Binding 和 Command 驱动。弹窗和账户页 code-behind 已尽量精简，只保留初始化和纯 UI 入口。

`DownloadPageView.xaml.cs` 中仍有较多滚动定位和动画逻辑，这些属于视图行为，不是业务逻辑。后续可考虑提取为 Behavior。

### ViewModels

主要 ViewModel：

- `MainViewModel`：Shell 状态、当前页面、导航命令、全局状态栏、窗口命令、子页面协调。
- `HomePageViewModel`：主页显示状态和启动命令。
- `AccountPageViewModel`：账户列表、添加/删除/重命名账户、皮肤/披风操作、账户相关弹窗状态。
- `DownloadPageViewModel`：Minecraft 版本列表、分类、搜索和选择状态。
- `GameManagementViewModel`：实例、Loader、Mod、Modrinth、创建实例、保存实例等游戏管理功能。

维护建议：

- `MainViewModel` 应保持轻量，不要再把页面业务塞回去。
- `AccountPageViewModel` 仍然偏重，是后续拆分的重点。
- `GameManagementViewModel` 也承载了较多业务，可继续拆成实例、版本、Mod 三个子 ViewModel 或 UseCase。

### Controls

可复用控件位于 `Launcher.App/Controls`：

- `DialogHost`
- `SecondaryMenuFrame`
- `SecondaryMenuOptionButton`
- `ListPageFrame`
- `ListPageItemButton`
- `VirtualizedListPageItemsControl`
- `AnimatedComboBox`
- `SvgIcon`
- `BackdropBlurBorder`
- `SmoothScrollBehavior`

这些控件主要负责 UI 表现和可复用交互，不应放业务逻辑。

### UI Services

位于 `Launcher.App/Services`：

- `StatusService`：全局状态消息。
- `WindowService`：最小化/关闭窗口。
- `ClipboardService`：剪贴板复制。
- `FilePickerService`：文件选择对话框。
- `AccountDialogService`：账户相关弹窗打开、关闭、尺寸动画和遮罩协调。
- `DialogOverlayService`：弹窗遮罩、模糊和动画。
- `NavigationMenuAnimationService`：侧边栏展开/收起动画。
- `PageTransitionService`：页面切换动画。
- `AcrylicWindow`、`BlurEffectWarmup`：窗口和模糊效果辅助。

这些属于 UI 基础设施，可以依赖 WPF，但不应依赖业务实现。

### Resources / Styles

资源和样式：

- `Launcher.App/Resources/ThemeResources.xaml`：主题 token，例如字体、颜色、间距、圆角。
- `Launcher.App/Styles/ControlStyles.xaml`：控件样式和模板。

后续建议继续拆分 `ControlStyles.xaml`，按按钮、弹窗、导航、列表、输入控件等拆成多个 ResourceDictionary。

## Application 层结构

### Services

接口：

- `ISettingsService`
- `IGameVersionService`
- `IGameInstanceService`
- `ILaunchService`
- `ILoaderProvider`
- `IModService`
- `IModrinthService`

实现：

- `GameInstanceService`

`GameInstanceService` 负责实例创建、默认实例、实例保存等业务流程。它通过 `IGameInstanceRepository` 隔离 JSON 和目录创建。

### Accounts

账户相关：

- `IAccountStore`
- `IMicrosoftAccountService`
- `AccountStore`
- `AccountMapper`
- `LauncherAccount`
- `AccountCapeOption`

注意：`LauncherAccount`、`AccountCapeOption` 当前在 Application 层，并带有 UI 绑定友好的属性。严格分层下可继续优化为：Application 返回纯 DTO，App 层再包装为可观察的 UI 模型。

### Repositories

- `IGameInstanceRepository`

仓储接口放在 Application，具体 JSON 实现在 Infrastructure。

## Domain 层结构

模型位于 `Launcher.Domain/Models`：

- `LauncherSettings`
- `LauncherAccountRecord`
- `LauncherCapeRecord`
- `LauncherDefaults`
- `GameInstance`
- `LoaderKind`
- `LoaderVersionInfo`
- `MinecraftVersionInfo`
- `LocalMod`
- `LauncherProgress`
- `ModrinthModels`

注意事项：

- `LauncherDefaults` 当前包含默认数据目录计算，使用了环境路径。严格领域模型中不应包含文件系统/环境逻辑，后续可迁到 Infrastructure 或配置服务。
- `ModrinthModels` 当前包含 JSON 序列化属性和 API 响应 DTO。更纯净的做法是把 Modrinth API DTO 移到 Infrastructure，Domain 只保留业务概念模型。

## Infrastructure 层结构

### Persistence

- `JsonSettingsService`：读写 `settings.json`。
- `JsonGameInstanceRepository`：读写 `instances.json`，创建实例目录结构。

默认数据位置：

```text
%AppData%/Launcher
```

典型文件/目录：

```text
settings.json
instances.json
accounts/microsoft/accounts.json
accounts/microsoft/avatars
instances/<instance>/mods
instances/<instance>/.launcher/disabled-mods
```

### Minecraft

- `GameVersionService`：通过 CmlLib 获取 Minecraft 版本列表。
- `LaunchService`：安装检查并启动 Minecraft 进程。
- `VanillaLoaderProvider`
- `FabricLoaderProvider`
- `PlaceholderLoaderProvider`

当前可用 Loader：

- Vanilla
- Fabric

占位 Loader：

- Forge
- NeoForge
- Quilt

### Modrinth

- `ModrinthService`：搜索 Modrinth，安装兼容 Mod 文件。

### Accounts

- `MicrosoftAccountService`：Microsoft 登录、账户缓存、Minecraft Profile API、皮肤上传、披风应用、改名、头像生成。

这是当前最重的 Infrastructure 类之一，后续可拆分为认证客户端、Minecraft Profile API 客户端、头像渲染服务、账户缓存服务等。

## 测试

测试项目：

- `Launcher.Tests`

当前覆盖内容包括：

- 设置 JSON 读写。
- 离线/正版账户顺序和披风缓存保存。
- 实例创建。
- Mod 导入、启停。
- Modrinth 搜索 facets。
- 下载页版本筛选、分类、搜索、选择。
- AccountStore 的账户导入和保存。

后续建议补充：

- `MainViewModel` 导航和 Shell 状态测试。
- `GameManagementViewModel` 创建实例、搜索 Mod、安装 Mod、状态上报测试。
- `AccountPageViewModel` 添加账户、删除账户、重命名、刷新披风、上传皮肤等命令测试。
- Infrastructure 测试和 Application 测试可以拆成独立测试项目，避免全部混在 `Launcher.Tests`。

## 构建与测试命令

常用命令：

```powershell
dotnet build Launcher.sln
dotnet test Launcher.sln
```

在受限沙箱或桌面工具中，WPF 项目构建可能需要读取本机 Microsoft SDK 缓存。如果出现类似 `Access to the path ... Microsoft SDKs is denied`，需要允许 `dotnet build` 或 `dotnet test` 访问 SDK 路径。

## 已知问题与维护提醒

1. 仍存在部分中文乱码字符串。
   - 已修复主页按钮文案，但 `MainViewModel`、`GameManagementViewModel`、`GameInstanceService` 等文件中仍可能有乱码状态文案或异常信息。
   - 修改中文时请确保文件以 UTF-8 保存。

2. `AccountPageViewModel` 职责偏重。
   - 它同时管理账户列表、弹窗流程、Microsoft 登录、皮肤、披风、重命名和持久化协调。
   - 后续建议拆成账户列表 VM、账户详情 VM、弹窗状态 VM，业务流程下沉到 Application UseCase。

3. `GameManagementViewModel` 仍偏大。
   - 它同时管理实例、版本、Loader、Mod、Modrinth 和设置保存。
   - 可拆成实例管理、版本选择、Mod 管理等子模块。

4. Domain 层还不够纯。
   - `LauncherDefaults` 和 `ModrinthModels` 有基础设施/外部 API 痕迹。

5. Infrastructure 当前依赖 WPF。
   - `MicrosoftAccountService` 使用 WPF 图像类型生成头像，因此 `Launcher.Infrastructure` 是 `net8.0-windows` 且 `UseWPF=true`。
   - 若要更干净，可将头像处理换成非 WPF 图像库，或单独拆成 Windows-specific adapter。

6. App 项目直接引用 Infrastructure。
   - 当前为了 DI 组合方便，这是可接受的启动器项目模式。
   - 约定：除 `App.xaml.cs` 组合根外，不要在 App 层其它类直接使用 Infrastructure 具体类型。

## 开发约定

- 优先保持 MVVM：View 只做 UI 和 Binding，ViewModel 只做状态、命令和交互协调。
- 不要在 ViewModel 中直接访问文件系统、HttpClient、CmlLib、Microsoft Auth 或 Modrinth API。
- 外部实现放 Infrastructure，接口放 Application。
- 业务模型优先放 Domain；外部 API DTO 不应长期放 Domain。
- UI 动画、窗口、剪贴板、文件选择可以留在 App Services。
- 新增样式时优先复用 `ThemeResources.xaml` 和 `ControlStyles.xaml`，避免在页面中堆大量重复 Setter。
- 修改 XAML 后建议至少运行 `dotnet build Launcher.sln`。
- 修改业务或服务后建议运行 `dotnet test Launcher.sln`。

## 新功能开发流程

新增功能时请按以下顺序进行：

1. 判断是否需要新增 Domain 模型或枚举。
2. 在 Application 层定义业务接口或 UseCase。
3. 在 Infrastructure 层实现外部依赖、文件读写或 API 调用。
4. 在 App 层新增 ViewModel、View、控件或样式。
5. 在 `App.xaml.cs` 或对应扩展方法中完成 DI 注册。
6. 添加或更新测试。
7. 运行 `dotnet build Launcher.sln`。
8. 若修改业务逻辑，运行 `dotnet test Launcher.sln`。

不要先从 ViewModel 直接写具体实现。

## MainViewModel 限制

`MainViewModel` 只能负责：

- 当前页面状态。
- 导航命令。
- 全局状态栏。
- 全局弹窗宿主。
- 窗口命令。
- 子页面 ViewModel 的组合。

`MainViewModel` 不应负责：

- Minecraft 版本下载。
- 游戏启动细节。
- Mod 搜索、安装、启停。
- Microsoft 登录流程。
- 皮肤、披风、改名 API 调用。
- JSON 文件读写。
- 实例目录创建。

## 禁止事项

- 不要恢复或继续使用 `Launcher.Core`。
- 不要为了快速实现功能而在 ViewModel 中直接 new Infrastructure 服务。
- 不要在 code-behind 中写业务流程。
- 不要把 API Response DTO 长期放在 Domain。
- 不要把 WPF 类型引入 Application 或 Domain。
- 不要在页面 XAML 中复制大量重复样式，应优先提取到 ResourceDictionary。
- 不要在没有必要的情况下大规模重命名公共类和文件，避免破坏现有测试和绑定。
