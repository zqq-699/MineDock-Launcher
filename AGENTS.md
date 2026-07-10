# AGENTS.md

本文档是 `launcher` 工作区的开发约定。所有 Codex / 自动化代理 / 后续开发者在修改代码前必须阅读并遵守。

## 项目目标

这是一个基于 WPF + C# / .NET 8 的 Minecraft Launcher。

目标：

* 遵循 MVVM。
* UI、业务逻辑、领域模型、基础设施、测试分层清晰。
* 不把新功能堆进 MainViewModel 或巨大 ViewModel。
* 不在 XAML / C# 中硬编码用户可见文本。
* 不在 XAML / C# 中硬编码主题颜色。
* 新功能必须接入日志记录。
* 新 UI 必须同时兼容深色主题和浅色主题。

## 项目结构

解决方案包含：

```text
Launcher.App
Launcher.Application
Launcher.Domain
Launcher.Infrastructure
Launcher.Tests
```

### Launcher.App

负责：

* View / ViewModel
* Controls / Behaviors / Converters
* UI Services
* Resources / Styles / Assets
* 主题、样式、动画、弹窗、窗口壳层

不负责：

* 文件读写
* 网络请求
* CmlLib 调用
* Microsoft Auth
* Minecraft / Modrinth API
* 复杂业务流程

### Launcher.Application

负责：

* 业务接口
* Application Service / UseCase
* 账户、实例、下载、启动、Mod 等业务编排
* Repository 接口

不负责：

* WPF
* XAML
* HttpClient 具体请求
* JSON 读写
* 文件系统实现
* CmlLib 具体调用

### Launcher.Domain

负责：

* 纯模型
* 枚举
* 设置状态
* 游戏实例模型
* Loader / Mod / 进度模型

不负责：

* WPF
* JSON
* 文件路径
* API DTO
* HttpClient
* CmlLib
* 外部服务实现

### Launcher.Infrastructure

负责：

* JSON 持久化
* 文件系统
* 路径提供
* CmlLib
* Minecraft 启动、下载、版本、修复
* Microsoft Auth / Profile API
* Modrinth API
* 本地 Mod 文件操作

### Launcher.Tests

负责：

* Application Service 测试
* ViewModel 测试
* Infrastructure 测试
* ResourceDictionary / 样式 / 主题测试
* Fake / Test helper

## 依赖方向

允许：

```text
Launcher.App -> Launcher.Application
Launcher.App -> Launcher.Domain
Launcher.App -> Launcher.Infrastructure 仅限 App.xaml.cs / DI 组合根

Launcher.Application -> Launcher.Domain

Launcher.Infrastructure -> Launcher.Application
Launcher.Infrastructure -> Launcher.Domain

Launcher.Tests -> 被测试项目
```

禁止：

```text
Domain -> Application / Infrastructure / App
Application -> App / Infrastructure
Infrastructure -> App
```

不要恢复 `Launcher.Core`，不要新增 `using Launcher.Core`。

## ViewModel 规则

ViewModel 只负责：

* 页面状态
* Binding 属性
* ICommand
* 调用 Application 接口
* 协调 UI Service
* 组合子 ViewModel

ViewModel 禁止：

* 直接读写文件
* 直接请求网络
* 直接调用 CmlLib
* 直接调用 Microsoft Auth
* 直接调用 Modrinth API
* 直接保存 JSON
* 写复杂业务流程

如果一个 ViewModel 同时负责多个功能区，必须拆分子 ViewModel 或下沉到 Application Service。

`MainViewModel` 只能负责 Shell 状态、导航、全局弹窗宿主、窗口命令和子页面组合。不要把下载、启动、实例、账户、Mod、皮肤、披风、设置保存等功能塞进 MainViewModel。

## View / code-behind 规则

View 和 `.xaml.cs` 只允许包含纯 UI 行为，例如：

* InitializeComponent
* 焦点处理
* 滚动定位
* 动画触发
* 视觉树查找
* 无法通过 Binding 表达的纯视图行为

禁止在 code-behind 写业务流程、文件读写、网络请求、实例创建、账户登录、Mod 管理、设置保存。

## 用户可见文本规则

所有用户可见文本必须放入：

```text
Launcher.App/Resources/Strings.resx
Launcher.App/Resources/Strings.cs
```

禁止在 XAML 或 C# 中直接硬编码用户可见文本。

允许保留在代码中的字符串：

* 内部日志 key
* API 参数
* 文件名
* 协议字段
* 测试数据
* 不直接展示给用户的技术异常信息

底层 exception message 不应直接展示给用户，必须转换为友好文案。

## 主题与颜色规则

所有主题相关颜色必须统一收口到资源字典，禁止硬编码。

禁止在以下位置直接写主题色：

* 页面 XAML
* Style / ControlTemplate
* Setter Value="#..."
* SolidColorBrush Color="#..."
* DropShadowEffect.Color="#..."
* GradientStop.Color="#..."
* Path.Fill / Path.Stroke 硬编码颜色
* C# 中 `new SolidColorBrush(...)`
* C# 中 `Color.FromRgb(...)`
* C# 中 `Brushes.White / Brushes.Black / Colors.*`

例外：

* Transparent
* 非主题用途的固定品牌素材色
* Win32 / DWM 必要系统常量
* Minecraft 方块、Forge/Fabric logo、皮肤、披风等内容型多色资产

主题资源结构：

```text
Launcher.App/Resources/Themes/Theme.Shared.xaml
Launcher.App/Resources/Themes/Theme.Dark.xaml
Launcher.App/Resources/Themes/Theme.Light.xaml
```

Shared 只放不随主题变化的资源：

* 字体
* 字号
* 间距
* 圆角
* 尺寸
* 动画时长
* Geometry
* 固定品牌蓝 / 危险红常量

Dark / Light 放所有主题相关资源：

* Brush.Text.*
* Brush.Icon.*
* Brush.Surface.*
* Brush.Border.*
* Brush.Control.*
* Brush.Input.*
* Brush.Button.*
* Brush.List.*
* Brush.Popup.*
* Brush.Dialog.*
* Brush.Scroll.*
* Color.Backdrop.*
* Color.Dwm.*

所有会随主题变化的 Foreground、Background、BorderBrush、Fill、Stroke、CaretBrush、SelectionBrush、Shadow、Popup、Dialog、Hover、Selected、Disabled 状态色必须使用 `{DynamicResource ...}`。

`StaticResource` 只允许用于：

* Geometry
* 尺寸
* 圆角
* 动画时长
* BasedOn
* Converter
* 不随主题变化的资源

新增 UI 必须同时检查深色和浅色主题，不允许只适配深色主题。

## SVG / 图标规则

UI 单色图标必须支持主题热切换。

要求：

* 单色 UI SVG 不保留硬编码 fill / stroke 颜色。
* SvgIcon 使用 Foreground 渲染 Fill。
* SvgIcon 使用 Stroke 渲染 Stroke；没有 Stroke 时使用 Foreground。
* SvgIcon 不得缓存 Brush、Pen、DrawingImage、ImageSource 或任何带主题颜色的渲染结果。
* 可缓存 Geometry 和非颜色结构信息。
* 按钮、菜单项、列表项里的图标和文字应优先绑定父控件 Foreground，以保证 Hover / Selected / Disabled 状态一致。

内容型 / 品牌型图标保留原色，例如：

* Minecraft 方块
* Forge / Fabric / NeoForge / Quilt logo
* 皮肤
* 披风
* 截图
* 多色实例图标

## 主题服务规则

主题切换由 App 层 ThemeService 负责。

ThemeService 负责：

* 读取 Theme / ThemeFollowSystem
* 计算 EffectiveTheme
* 动态替换 Dark / Light ResourceDictionary
* 在 UI 线程切换主题
* 发布 EffectiveThemeChanged
* 跟随 Windows 系统主题变化

settings.json 只保存：

```text
Theme
ThemeFollowSystem
```

不要把 EffectiveTheme 写入 settings.json。

新增主题、控件或页面时，必须保证：

* 可在运行时热切换
* 不需要重启启动器
* 深色和浅色都能正确显示
* 文本、图标、弹窗、下拉、Hover、Selected、Disabled 状态都能切换

## 日志规则

新增功能必须接入日志记录。

日志应记录：

* 关键业务开始 / 成功 / 失败
* 下载、安装、启动、修复、账户、皮肤、披风、设置保存等关键流程
* 失败原因、异常类型、错误码、诊断路径
* 重要参数摘要

日志禁止记录：

* access token
* refresh token
* Authorization header
* Microsoft / Minecraft 敏感凭据
* 未脱敏的账号敏感数据

敏感信息必须脱敏为：

```text
<redacted>
```

普通 launcher.log 只记录摘要和诊断文件路径。
详细启动失败信息写入 launch-diagnostics 日志。

新增功能如果有失败场景，必须考虑：

* 用户友好错误提示
* 日志记录
* 诊断信息
* 测试覆盖

## 新功能开发流程

新增功能时必须按顺序设计：

1. 判断是否需要 Domain 模型或枚举。
2. 在 Application 定义接口或 UseCase。
3. 在 Infrastructure 实现文件、网络、CmlLib 或外部依赖。
4. 在 App 新增 ViewModel / View / Control / Resource / Style。
5. 在 DI 中注册服务。
6. 接入日志。
7. 接入 Strings.resx。
8. 适配深色和浅色主题。
9. 按风险最小化添加或更新测试，避免穷举和重复覆盖。
10. 运行构建和测试。

不要从 ViewModel 直接开始写外部实现。

## 样式和控件复用规则

不要复制大量重复 XAML。

以下内容必须优先抽到 ResourceDictionary、Style、ControlTemplate、UserControl 或 CustomControl：

* 按钮
* 输入框
* 下拉框
* 列表项
* 卡片
* 弹窗
* 滚动条
* 页面标题
* 间距
* 字体
* 圆角
* 阴影
* 颜色

新增样式前，先检查已有样式是否可复用。

## 长列表性能规则

涉及版本列表、Mod 列表、实例列表、账户列表、搜索结果时，必须使用 WPF 原生虚拟化。

要求：

* ListBox / ListView + VirtualizingStackPanel
* `VirtualizingPanel.IsVirtualizing="True"`
* `VirtualizingPanel.VirtualizationMode="Recycling"`
* 不要在虚拟化列表外层套普通 ScrollViewer
* 不要恢复手写虚拟列表
* 不要滚动时频繁 Clear / Add 重建集合
* 列表项避免复杂阴影、模糊、大图片和过深视觉树

## 文件放置规则

新增代码按职责放置：

```text
View                -> Launcher.App/Views
ViewModel           -> Launcher.App/ViewModels
Control             -> Launcher.App/Controls
Behavior            -> Launcher.App/Behaviors
UI Service          -> Launcher.App/Services
Theme / Resource    -> Launcher.App/Resources
Style               -> Launcher.App/Styles
用户文本            -> Launcher.App/Resources/Strings.resx

业务接口            -> Launcher.Application/Services
UseCase / Service   -> Launcher.Application/Services
Repository 接口     -> Launcher.Application/Repositories

模型 / 枚举         -> Launcher.Domain/Models

JSON 持久化         -> Launcher.Infrastructure/Persistence
文件系统            -> Launcher.Infrastructure/FileSystem
Minecraft / CmlLib  -> Launcher.Infrastructure/Minecraft
Microsoft 账户      -> Launcher.Infrastructure/Accounts
Modrinth            -> Launcher.Infrastructure/Modrinth

测试                -> Launcher.Tests
```

## 测试规则

测试采用“风险驱动、最小充分”原则。不要以测试数量、代码行数或覆盖率为目标，也不要默认要求每个属性、命令、分支和状态都单独写测试。

只有以下情况才应新增或保留测试：

* 曾经真实发生过、容易复发且影响明显的回归。
* 可能造成用户数据丢失、目录误删、文件损坏或安全问题。
* 下载、安装、启动、修复、更新等关键流程的主要成功路径。
* 并发、取消、重试、回滚、事务清理和故障恢复。
* 复杂解析、兼容逻辑、重要边界条件和外部协议映射。
* 多语言资源 Key / 占位符、主题资源等低成本契约检查。

不要为以下内容编写详细测试：

* 简单 getter / setter、默认值、属性通知和直接参数转发。
* 每个弹窗的打开、关闭、取消以及每个中间 UI 状态。
* ViewModel 中低风险的按钮可用性、筛选切换和页面导航细节。
* 同一实现路径下的每个枚举值、Loader、资源类型或 HTTP 状态码。
* 框架自身行为、静态常量、固定文案内容和发布脚本的逐行断言。
* 一般通过一次后极少可能因后续改动回归的实现细节。

测试设计要求：

* 每个等价行为只保留一个代表场景；同类输入优先使用 Theory。
* 优先断言最终结果和安全不变量，不断言无关的中间调用次数或内部实现顺序。
* 大型测试文件必须拆除重复 Setup、Fake 和排列组合；不为测试复制完整业务对象图。
* 新功能只补充与新增风险直接相关的测试，不顺带扩写无关覆盖。
* 修复 Bug 时，只有在 Bug 容易复发且测试稳定、简短时才增加回归测试。
* 如果新增测试会明显扩大 `Launcher.Tests`，先删除或合并已有重复测试。
* 测试应稳定、可读、短小；出现偶发失败的细粒度 UI 测试应简化或删除。

优先测试：

* Application Service
* Repository / Infrastructure 的数据安全和回滚行为
* 下载 / 安装 / 启动 / 修复 / 更新关键流程
* 并发、取消、重试和边界条件
* ViewModel 的少量核心用户流程
* ResourceDictionary 和多语言资源契约

测试禁止依赖：

* 真实 Microsoft 登录
* 不稳定网络
* 用户本地真实数据
* 真实大型目录

修改 XAML、资源字典、样式后，至少运行：

```powershell
dotnet build Launcher.sln
```

修改业务逻辑、ViewModel、Service、Repository 后，运行：

```powershell
dotnet test Launcher.sln
```

## 修改前检查

写代码前确认：

* 这个功能属于 UI、业务、领域模型还是基础设施？
* 是否需要新增接口？
* 是否会让 MainViewModel 变重？
* 是否需要拆分 ViewModel？
* 是否有用户可见文本？
* 是否有主题颜色？
* 是否同时适配深色和浅色？
* 是否需要日志？
* 是否需要测试？
* 是否会破坏依赖方向？

## 修改后检查

完成后确认：

* 是否符合 MVVM？
* ViewModel 是否没有直接访问文件、网络、CmlLib、外部 API？
* code-behind 是否没有业务逻辑？
* 用户文本是否进入 Strings.resx？
* 主题颜色是否没有硬编码？
* 深色和浅色主题是否都正常？
* 新功能是否记录日志？
* 敏感信息是否脱敏？
* 是否没有恢复 Launcher.Core？
* 是否没有破坏长列表虚拟化？
* 是否运行 build？
* 涉及业务时是否运行 test？

## 禁止事项

禁止：

* 把新功能塞进 MainViewModel。
* 把多个业务塞进一个巨大 ViewModel。
* 在 ViewModel 中 new Infrastructure 服务。
* 在 ViewModel 中读写文件。
* 在 ViewModel 中请求网络。
* 在 ViewModel 中调用 CmlLib。
* 在 ViewModel 中调用 Microsoft Auth。
* 在 ViewModel 中调用 Modrinth API。
* 在 code-behind 写业务流程。
* 在 XAML / C# 中硬编码用户可见文本。
* 在 XAML / C# 中硬编码主题颜色。
* 只适配深色主题，不适配浅色主题。
* 新功能不写日志。
* 日志泄露 token。
* API DTO 放入 Domain。
* WPF 类型引入 Domain 或 Application。
* 无必要大规模重命名公共类、绑定属性或资源 key。
* 删除现有功能或破坏现有 UI 动画。
* 恢复旧的手写虚拟列表。

## 总原则


```text
UI 归 UI
业务归 Application
模型归 Domain
外部实现归 Infrastructure
测试归 Tests
文本归 Strings.resx
样式归 ResourceDictionary
主题归 ThemeService
日志要记录
敏感要脱敏
深浅主题都要适配
颜色不准硬编码
长逻辑要拆
大 ViewModel 要拆
MainViewModel 不准变垃圾桶
```
