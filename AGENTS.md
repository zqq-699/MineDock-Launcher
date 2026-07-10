# AGENTS.md

本文档是 `launcher` 工作区的开发约定。所有 Codex、自动化代理和后续开发者在修改代码前必须阅读并遵守。

## 项目原则

这是一个基于 WPF、C# 和 .NET 8 的 Minecraft Launcher。

所有改动必须遵循以下原则：

- 使用 MVVM，保持 UI、业务、领域模型、基础设施和测试分层清晰。
- 优先复用现有服务、控件、样式和资源，避免重复实现。
- 不把新功能堆进 `MainViewModel` 或其他巨大 ViewModel。
- 用户可见文本统一使用资源文件，主题颜色统一使用资源字典。
- 新功能必须考虑日志、友好错误提示、深浅主题和必要测试。
- 保持改动聚焦，不做无关重构，不破坏现有功能和动画。

## 项目结构与职责

解决方案包含五个项目：

```text
Launcher.App
Launcher.Application
Launcher.Domain
Launcher.Infrastructure
Launcher.Tests
```

- `Launcher.App`：WPF View、ViewModel、控件、UI Service、资源、样式、主题和窗口壳层。
- `Launcher.Application`：业务接口、Use Case、Application Service、业务编排和 Repository 接口。
- `Launcher.Domain`：纯领域模型、枚举和值对象，不依赖 UI 或外部实现。
- `Launcher.Infrastructure`：文件、JSON、网络、CmlLib、账户、Minecraft 和第三方服务实现。
- `Launcher.Tests`：Application、Infrastructure、ViewModel、资源契约及必要的回归测试。

新增代码应按职责放入对应项目和现有功能目录；先检查已有结构，不为单个类随意创建新层级。

## 依赖方向

允许：

```text
Launcher.App -> Launcher.Application
Launcher.App -> Launcher.Domain
Launcher.App -> Launcher.Infrastructure（仅限 App.xaml.cs / DI 组合根）

Launcher.Application -> Launcher.Domain

Launcher.Infrastructure -> Launcher.Application
Launcher.Infrastructure -> Launcher.Domain

Launcher.Tests -> 被测试项目
```

禁止：

```text
Launcher.Domain -> Launcher.Application / Launcher.Infrastructure / Launcher.App
Launcher.Application -> Launcher.Infrastructure / Launcher.App
Launcher.Infrastructure -> Launcher.App
```

不要恢复 `Launcher.Core`，不要新增 `using Launcher.Core`。

## Application、Domain 与 Infrastructure

- 新业务先判断是否需要 Domain 模型，再在 Application 定义接口或业务服务。
- Application 负责业务规则和流程编排，不依赖 WPF、文件系统、网络客户端或具体第三方 SDK。
- Domain 保持纯净，不放 API DTO、JSON 细节、文件路径、WPF 类型或外部服务实现。
- Infrastructure 实现 Application 定义的接口，负责持久化、网络、文件系统和外部依赖。
- API DTO 和外部协议模型留在 Infrastructure，并在边界处转换为 Domain 模型。
- App 通过依赖注入使用 Application 接口，不在 ViewModel 中创建 Infrastructure 服务。
- 新服务必须在现有 DI 组合根中注册，不使用隐藏的服务定位器绕过依赖关系。

## ViewModel 与 View

ViewModel 只负责页面状态、Binding 属性、命令、调用 Application 接口、协调 UI Service 和组合子 ViewModel。

ViewModel 禁止直接：

- 读写文件、JSON 或用户目录。
- 请求网络或调用外部 API。
- 调用 CmlLib、Microsoft Auth、Modrinth 等具体实现。
- 承担复杂安装、下载、启动、修复或持久化流程。

复杂页面应拆分职责明确的子 ViewModel，复杂业务应下沉到 Application Service。

`MainViewModel` 仅负责 Shell 状态、导航、全局弹窗宿主、窗口命令和页面组合。下载、启动、实例、账户、资源和设置等功能必须留在各自页面或业务服务中。

View 和 `.xaml.cs` 只允许包含无法通过 Binding 合理表达的纯 UI 行为，例如焦点、滚动、动画和视觉树处理。禁止在 code-behind 中编写业务流程、文件操作、网络请求或设置保存。

## 用户文本与错误提示

所有用户可见文本必须来自：

```text
Launcher.App/Resources/Strings.resx
Launcher.App/Resources/Strings.*.resx
Launcher.App/Resources/Strings.cs
```

- 不在 XAML 或 C# 中硬编码按钮、标题、提示、错误等用户文案。
- 新增资源 Key 时保持现有命名方式，并同步项目支持的语言资源。
- API 参数、协议字段、文件名、日志模板和不展示给用户的技术信息可以留在代码中。
- 不直接向用户展示底层 exception message；应转换为可理解、可操作的友好提示。

## 主题、样式与图标

主题和强调色资源位于：

```text
Launcher.App/Resources/Themes/Shared.xaml
Launcher.App/Resources/Themes/Dark.xaml
Launcher.App/Resources/Themes/Light.xaml
Launcher.App/Resources/Themes/Accents/
```

- `Shared.xaml` 只放不随主题变化的尺寸、间距、字体、Geometry 等共享资源。
- 深浅主题颜色分别放入 `Dark.xaml` 和 `Light.xaml`；强调色放入 `Themes/Accents`。
- 页面、Style、ControlTemplate 和 C# 中不得硬编码会随主题变化的颜色或 Brush。
- 会随主题或控件状态变化的资源必须使用 `{DynamicResource ...}`。
- `StaticResource` 仅用于不会随运行时主题变化的资源、Converter、Geometry 和 `BasedOn`。
- 新 UI 必须支持运行时热切换，并同时检查深色、浅色、Hover、Selected 和 Disabled 状态。
- 新增样式前先复用现有 ResourceDictionary、Style、ControlTemplate、UserControl 或 CustomControl。
- 不复制大段重复 XAML，不在页面中重复定义通用间距、圆角、字体、阴影或控件样式。

单色 UI 图标必须继承 `Foreground` 或 `Stroke`，支持主题和控件状态热切换；不得缓存带主题颜色的 Brush、Pen 或渲染结果。品牌 Logo、Minecraft 内容、皮肤、披风、截图等内容型多色资产可以保留原色。

主题切换继续由 App 层 `ThemeService` 负责。不要把有效主题计算或资源字典替换逻辑散落到 View、ViewModel 或其他服务中。

## 长列表性能

版本、Mod、实例、账户和搜索结果等长列表必须使用 WPF 原生虚拟化：

```text
VirtualizingPanel.IsVirtualizing="True"
VirtualizingPanel.VirtualizationMode="Recycling"
```

- 优先使用 `ListBox` / `ListView` 与 `VirtualizingStackPanel`。
- 不在虚拟化列表外层套普通 `ScrollViewer`，不恢复手写虚拟列表。
- 不在滚动或筛选时频繁 `Clear` / `Add` 重建整个集合。
- 列表项避免复杂阴影、模糊、大图和过深的视觉树。

## 日志、安全与诊断

新增功能必须使用现有日志体系记录关键业务的开始、成功、失败和必要参数摘要。

- 失败日志应包含足够的异常、错误码和诊断路径，便于定位问题。
- 普通日志只保留必要摘要；大量或敏感诊断内容写入现有专用诊断日志。
- 日志不得包含 access token、refresh token、Authorization Header 或未脱敏的账户凭据。
- 必须记录敏感字段时统一写为 `<redacted>`，不要依赖调用方自行隐藏。
- 失败场景同时考虑用户友好提示、日志、清理或回滚以及必要测试。

不要在日志中输出完整环境变量、请求头、认证响应、用户私有文件内容或其他无关敏感信息。

## 测试原则

测试采用“风险驱动、最小充分”原则，不以数量、行数或覆盖率为目标。

优先覆盖：

- 可能造成数据丢失、误删、文件损坏或安全问题的行为。
- 下载、安装、启动、修复、更新等关键流程的主要成功路径。
- 并发、取消、重试、回滚、清理和故障恢复。
- 复杂解析、兼容逻辑、重要边界及外部协议映射。
- 稳定且容易复发的回归，以及资源 Key、主题等低成本契约。

通常不为简单 getter/setter、属性通知、直接参数转发、框架行为或低风险 UI 中间状态编写穷举测试。

- 每个等价行为保留代表场景，同类输入优先使用 Theory。
- 优先断言最终结果和安全不变量，避免绑定内部调用次数和实现顺序。
- 复用现有 Fake 和 Test Helper，不为测试复制完整业务对象图。
- 测试必须稳定、短小，不依赖真实 Microsoft 登录、不稳定网络、用户真实数据或大型目录。
- 新功能只增加与新增风险直接相关的测试；先合并或删除明显重复的旧测试。

## 构建与验证

修改 XAML、资源字典、样式或 UI 代码后，至少运行：

```powershell
dotnet build Launcher.sln
```

修改业务逻辑、ViewModel、Service、Repository 或 Infrastructure 后，运行：

```powershell
dotnet test Launcher.sln
```

仅修改文档时无需构建，但应检查 Markdown、路径、命令和空白错误。

## 实施检查表

修改前：

- 阅读相关实现、测试和资源，确认代码所属层级与依赖方向。
- 搜索可复用的接口、服务、控件、样式和资源 Key。
- 确认是否涉及用户文本、主题、日志、敏感数据、长列表或数据安全。
- 检查工作树，保留用户已有和与任务无关的改动。

实现时：

- 按 Domain → Application → Infrastructure → App → DI 的职责顺序组织新功能。
- 保持改动范围最小，不无故重命名公共类、Binding 属性或资源 Key。
- 不删除现有功能，不破坏现有动画、主题热切换和列表虚拟化。
- 对失败路径提供友好提示，并记录不泄密的诊断信息。

完成后：

- 确认 ViewModel 和 code-behind 未承担文件、网络或复杂业务逻辑。
- 确认用户文案已资源化，主题颜色未硬编码，深浅主题均可用。
- 确认日志覆盖关键流程且没有 token、凭据或隐私数据。
- 确认没有引入 `Launcher.Core`，没有破坏项目依赖方向。
- 按改动类型运行必要的 build/test，并报告未运行的验证及原因。

总原则：UI 归 App，业务归 Application，模型归 Domain，外部实现归 Infrastructure，测试归 Tests；文本资源化、颜色主题化、日志可诊断、敏感信息必须脱敏。
