# Winwright 项目分析

本文梳理 Winwright 这个项目到底是什么、怎么搭的、怎么跑，供 WPF / Windows UI 调试方向研究参考。引用到的源码文件一律给 Windows 绝对路径，重要的类名和方法名会点出来方便定位。

## 分析范围和项目定位

先说清楚分析的边界。Winwright 的根目录是 `G:\Projects\Winwright`，里面只有 `bin\`（编译产物，一堆 dll）和 `refsrc\`（reference source，参考源码）。真正的源码、解决方案、工程文件全在 `G:\Projects\Winwright\refsrc` 下，本文分析的就是这一层。`bin\` 里的东西是构建结果，不作为分析对象。

`refsrc` 里是一个名为 `Civyk.WinWright` 的 .NET 解决方案，见 `G:\Projects\Winwright\refsrc\Solution.sln`，由四个工程组成：

- `Civyk.WinWright.Selectors` —— 纯 schema 与选择器编译，零业务依赖
- `Civyk.WinWright.Core` —— UI 自动化引擎核心
- `Civyk.WinWright.Runner` —— 脚本回放与自愈
- `Civyk.WinWright.Mcp` —— MCP 服务端与命令行入口

一句话定位：Winwright 是一个把 Windows 桌面 UI 自动化（基于 FlaUI / UI Automation）包装成 MCP 工具，并附带脚本录制、回放、自愈能力的服务端程序。它对标的是 Playwright 那一套「定位 / 操作 / 断言 / 快照 / 录制回放」的体验，只不过对象从浏览器换成了原生桌面应用（WPF、WinForms、Win32 都行），同时还顺带把一批 Windows 系统操作（进程、文件、注册表、网络、电源、计划任务……）也做成了 MCP 工具。项目名里的「Wright」加上命名空间 `Civyk.WinWright`，可以理解成「Windows 版的 Playwright」。

虽然中心类型叫 `WpfSession`，但它实际能驱动任何走 UIA 的桌面程序，框架识别只是辅助。这一点后面会细说。

## 技术栈和工程组织

工程统一是 .NET 9、x64、`LangVersion 11`、允许 unsafe，见各 `*.csproj`。Selectors 为了能被任何上层复用，特意降到 `netstandard2.0`，只引一个 `System.Text.Json`；Core、Runner、Mcp 都是 `net9.0`。

依赖项有一点要留意：四个工程的 `csproj` 都是通过 `<HintPath>` 直接引用 `G:\Projects\Winwright\bin\` 下的 dll，而不是走 NuGet。也就是说 FlaUI、ModelContextProtocol、InputSimulatorStandard、Microsoft.AspNetCore.* 这些二进制是预先放进 `bin\` 的，构建时按路径取。主要的几路依赖：

- **FlaUI.Core / FlaUI.UIA3** —— UI 自动化的底座。UIA3 走的是现代的 `IUIAutomation` COM 接口，比老的 UIA2 兼容性好，WPF 默认就支持。
- **ModelContextProtocol / ModelContextProtocol.Core / ModelContextProtocol.AspNetCore** —— 官方 C# SDK 的 MCP 协议实现，Winwright 用它的 `[McpServerToolType]` / `[McpServerTool]` 特性来声明工具。
- **Microsoft.AspNetCore.Authentication.Negotiate** —— 给 HTTP 传输提供 Windows 鉴权（Kerberos / NTLM）。
- **InputSimulatorStandard + 自己 P/Invoke 的 SendInput** —— 模拟键鼠。
- **System.Management / System.ServiceProcess.ServiceController** —— 进程和服务管理。

工程的引用关系是分层且单向的：Selectors 在最底，Core 和 Runner 都引它，Core 不引 Runner；Mcp 在最上层，把另外三个全引上，是对外的汇总入口。命名空间基本和目录一一对应（比如 `Civyk.WinWright.Core.Session` 对应 `Civyk.WinWright.Core\Civyk.WinWright.Core.Session\`），看目录就能猜到命名空间。

> 补一句：`refsrc` 里的 C# 代码看起来像是反编译产物（方法名里偶有 `_003CMain_003E_0024` 这种编译器生成的痕迹，`finally` 里还有 `if (cts != null)` 之类对源码来说无意义但对反编译来说常见的写法）。不影响阅读，但要知道它不是手写的原始风格。

## 启动入口：一个 CLI，六种命令

程序入口在 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Program.cs`。它本身 `OutputType` 是 `Library`，但 `Program` 里有静态入口，编译后会作为可执行程序跑。启动后第一件事是调 `NativeMethods.SetProcessDpiAwarenessContext` 把进程设成 Per-Monitor DPI Aware（`G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp\NativeMethods.cs`），这样截屏和坐标计算才不会因为系统缩放错位。

然后按第一个参数分发到六个子命令：

`winwright mcp` 是最常见的用法，起一个走 stdio 的 MCP server。流程是建一个 `HostApplicationBuilder`，加载配置（先找 exe 同目录的 `winwright.json`，再找 `%AppData%\WinWright\winwright.json`），注册 `SessionRegistry`、`AuditLogger`、`BrowserRegistry` 等单例，然后用反射把程序集里所有标了 `[McpServerToolType]` 的类收集起来，交给 `AddMcpServer().WithStdioServerTransport().WithTools(...)` 注册成 MCP 工具，最后 `RunAsync`。

`winwright serve [--port N]` 起的是 HTTP 传输，默认端口 8765，实现见 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Transport\HttpTransportHost.cs`。它本质是个 ASP.NET Core 应用：MCP 走 `MapMcp("/mcp")`（Streamable HTTP），事件订阅走一个额外的 SSE 端点 `/sse/watch`。HTTP 模式还能开 TLS、Windows Negotiate 鉴权、IP 白名单、按 IP 限流——这些都是为「远程让别人连过来操作你这台机器」准备的，本地调试用不上。

`winwright run <script.json>` 回放一个录制好的自动化脚本，支持 `--format text|junit`、`--output`、`--screenshots`。它会先判断脚本里有没有用到浏览器工具或系统工具，按需创建 `BrowserReplayDispatcher` / `SystemReplayDispatcher`，再交给 `ScriptRunner` 跑。

`winwright heal <script.json>` 对一个脚本做「自愈」：探测每一步的选择器还能不能定位到，定位不到就用模糊匹配找最像的元素，按相似度决定是自动替换、给建议还是放弃。`--min-confidence` 控制自动替换的阈值，默认 0.7。

`winwright inspect <pid>` 是个独立的诊断命令，给一个进程 PID，它开一根 STA 线程，用 `UIA3Automation` 把那个进程的所有顶层窗口及其子元素（最多 8 层）dump 成 JSON 打到 stdout，字段是 `controlType / name / automationId / className / children`。调试选择器问题时很有用。

`winwright doctor` 做环境自检：操作系统版本、.NET 运行时、UIA3 能不能拿到 Desktop，每项打印 OK / FAIL。

## 工具是怎么定义出来的

Winwright 的 MCP 工具走的是 ModelContextProtocol C# SDK 的标准模式，不用自己写注册逻辑。形态很统一，随便看一个就懂，比如 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Tools\LifecycleTools.cs` 里的 `ww_launch`：

一个标了 `[McpServerToolType]` 的静态类，里面每个静态方法打 `[McpServerTool(Name = "ww_xxx")]`，方法参数用 `[Description(...)]` 描述，SDK 会把这些描述连同参数类型自动生成 JSON Schema 暴露给模型。方法签名里除了真正的业务参数，还能让 DI 容器注入 `SessionRegistry`、`WinWrightConfig`、`AuditLogger`、`RequestContext`、`IHttpContextAccessor` 这些——SDK 会按类型自动填，业务参数按名字从模型请求里取。

工具方法统一返回 `Task<string>`，这个字符串就是给模型看的 JSON。成功走 `ToolHelper.ToJson(...)`（见 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp\ToolHelper.cs`），失败走 `ToolHelper.HandleException`。`HandleException` 把 `WinWrightException` 拆成 `{code, message, suggestion, details}` 的标准错误结构，`COMException` 翻译成 `action_failed`，参数校验异常翻译成 `selector_invalid`，兜底是 `unknown_error`。也就是说模型拿到的永远是结构化 JSON，不会是裸异常栈。

工具命名一律 `ww_` 前缀，按职能分到二十来个静态类里。完整清单有 65 个工具，这里不逐个列，按职能归类说一下（每个文件都在 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Tools\` 下）：

会话生命周期（`LifecycleTools.cs`）：`ww_launch`、`ww_attach`、`ww_close`、`ww_list_windows`。`ww_launch` 支持 `allowElevated`（走 ShellExecute + `runas` 触发 UAC）、`frameworkHint`、`mainWindowSelector`（启动后轮询等某个元素出现再返回），还会校验 `AllowedExecutables` 白名单。

元素查询和树导出（`QueryTools.cs`、`InspectTools.cs`）：`ww_query` 解析选择器并可选地创建 handle，`ww_dump_tree` 把原始 UIA 子树 dump 出来（text 或 json，可带 patterns），`ww_inspect` 看单个元素属性，`ww_get_attribute` / `ww_get_value` 读属性和值，`ww_find_by_description` 用自然语言描述找元素，`ww_label_map` 导出控件-标签映射。

快照和状态对比（`SnapshotTools.cs`）：`ww_get_snapshot` 出一份精简语义树，`ww_get_state_hash` 给当前 UI 算 sha256，`ww_diff_state` 比较两个状态哈希的增删改，`ww_assert_snapshot` 断言当前 UI 和某个历史快照一致。后三个配合使用就是一套 visual regression。

交互操作（`InteractionTools.cs`、`ActionTools.cs`、`GridTools.cs`）：`ww_click`、`ww_type`、`ww_set_value`、`ww_clear`、`ww_invoke`、`ww_focus`、`ww_set_checked`、`ww_expand`、`ww_keyboard`、`ww_hover`、`ww_scroll`、`ww_drag_drop`、`ww_select`、`ww_select_text`、`ww_screenshot`，外加表格相关的 `ww_get_table_data` / `ww_get_cell` / `ww_set_cell`。

断言和等待（`QueryTools.cs` 的 `ww_assert`、`WaitTools.cs`）：`ww_assert` 是「永不抛错」的断言，返回 `{passed, assertion, expected, actual, elapsedMs}`，支持 `value_equals / value_contains / is_visible / is_enabled / is_checked / exists / count_equals` 等一堆断言类型；`ww_wait` 是显式等待元素进入某状态（attached / visible / enabled / gone / checked / unchecked / focused）。

对话框（`DialogTools.cs`）：`ww_handle_dialog`、`ww_handle_file_dialog`、`ww_expect_dialog`。这部分针对 Windows 原生 MessageBox（`#32770` 窗口类）和文件对话框做了大量 Win32 回退处理。

录制和观察（`RecordTools.cs`）：`ww_record`（开始 / 停止 / 推入 test case）、`ww_assert_value`、`ww_watch`（订阅事件）。

浏览器（`BrowserTools.cs`、`BrowserElementTools.cs`）：`ww_browser_session`、`ww_browser_page`、`ww_browser_element`、`ww_browser_advanced`。这是 Winwright 里独立的一条线，通过 CDP 连本地 Chromium，和 chrome-devtools-mcp 那套是同一个生态位，后面单独说。

Windows 系统操作（一大堆 `*Tools.cs`）：这块是 Winwright 区别于纯 UI 自动化工具的地方。每个工具都走「单工具 + action 参数」的模式，一个工具承载多种操作。`ww_system`（SystemTools.cs）一个工具就管 `info / shell / notification / power / lock_screen` 五种，其中 shell、power、lock_screen 都要过权限；`ww_file` 管文件读写删列表、`ww_process` 管进程、`ww_registry` 管注册表、`ww_service` 管 Windows 服务、`ww_task` 管计划任务、`ww_network` 管网络接口和连通性、`ww_env` 管环境变量、`ww_clipboard` 管剪贴板。这些工具的存在意味着 Winwright 想做的不止是「驱动 UI」，而是「通过 MCP 远程操作一整台 Windows 机器」。

窗口管理（`WindowManagementTools.cs`、`UtilityTools.cs`）：`ww_window_resize`、`ww_window_state`（最小化 / 最大化 / 还原）、`ww_activate_window`，以及一批杂项 `ww_count`、`ww_is_alive`、`ww_release_handle`、`ww_get_session_info`、`ww_get_tree_path`、`ww_get_schema`。

自愈（`HealTools.cs`）：`ww_heal_script`，在 MCP 侧暴露的脚本自愈入口。

## 核心引擎：session、线程模型、元素定位

Core 这一层是整个项目的重头戏。中心是 `WpfSession`，路径 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Session\WpfSession.cs`。每个被驱动的目标进程对应一个 session，它聚合了这一进程自动化要用到的全部部件：

`UIA3Automation` 是 FlaUI 的自动化对象，所有 UIA 调用都通过它。`UiaDispatchThread`（`G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Threading\UiaDispatchThread.cs`）是 Winwright 自己实现的一根专用 STA 线程，这是整套设计能成立的关键。原因是 COM 的 UIA 大量接口要求调用方在 STA 线程上，而且很多操作（事件订阅、模式调用）还依赖 Windows 消息泵。Winwright 的做法是：每个 session 启一根名叫 `UIA-STA-Dispatch` 的后台 STA 线程，内部跑一个 `BlockingCollection<Func<Task>>` 队列，所有 UIA 调用都通过 `Dispatch.InvokeAsync(...)` 排到这根线程上执行；每处理完一个任务还会 `DrainMessageQueue`，用 `PeekMessage / TranslateMessage / DispatchMessage` 抽干最多 50 条待处理消息，保证 COM 和窗口消息不堵。这样一来上层就可以随便用多线程调 Winwright 的 API，底层始终串行化到单根 STA 线程，避免了 UIA 在多线程下各种时序坑。

session 里其它几个部件：`HandleRegistry` 管元素 handle（对标 playwright 的 element handle），`WindowTracker` 跟踪目标进程的顶层窗口，`HealthMonitor` 监控进程死活（死了发 `Died` 事件，自动从注册表摘掉），`DialogWatcher` 后台盯对话框，`SnapshotCache` 缓存状态快照，`EventWatcher` 把 UIA 结构 / 值 / 焦点 / 窗口事件泵成 `EventMessage` 流，`Recorder` 在录制态累积每一步动作。

`SessionRegistry`（同目录 `SessionRegistry.cs`）是 session 的工厂和仓库，管 `LaunchAsync` / `AttachAsync` / `CloseAsync` / `Get`。它做了几件值得说的事：一是并发上限（默认 10 个 session）和按用户配额（远程模式下 `MaxSessionsPerUser`），防止一个调用方把机器榨干；二是 attach 时按 PID 加锁，同一个进程不会被并发 attach 两次，重复 attach 直接返回已存在的 session；三是保护系统进程，PID < 10 直接拒（避免 attach 到 idle / system 之类的内核进程）；四是 launch 支持 `allowElevated`（走 UAC）和自定义环境变量，但两者互斥（因为 `UseShellExecute=true` 时不能传 env）；五是 launch 后会等主窗口出现，可选地等一个 `mainWindowSelector` 命中的元素出现，启动失败会 kill 进程并回滚计数。

框架识别在 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Session\FrameworkDetector.cs`，逻辑很简单：枚举进程模块，看有没有 `PresentationFramework.dll`（WPF）或 `System.Windows.Forms.dll`（WinForms），组合出 `Wpf / WinForms / Hybrid / Win32 / Unknown`。虽然类名叫 `WpfSession`，但实际驱动哪种都行，框架标记更多是给上层做策略用的。

### 选择器：自研 DSL

Winwright 没有直接用 FlaUI 的条件 API，而是自己设计了一套选择器 DSL，编译成 UIA Condition + 托管过滤两层。相关代码集中在 `Civyk.WinWright.Selectors` 工程里，分三段：

`Civyk.WinWright.Selectors.Parsing\` 是手写的词法 + 语法分析器（`SelectorLexer` + `SelectorParser`），`Civyk.WinWright.Selectors.Ast\` 是语法树节点（`SelectorNode / SegmentNode / BaseTokenNode / FilterNode / IndexNode` 等），`Civyk.WinWright.Selectors.Execution\` 是编译器 `SelectorCompiler`，把 AST 编成可执行的 `CompiledSelector`（一组 `CompiledSegment`）。

这套 DSL 支持的语法大致长这样（具体能写什么可以去看 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Selectors\Civyk.WinWright.Selectors.Execution\SelectorCompiler.cs` 里的键白名单和校验）：

- 简写 ID：`#btnSubmit`
- 类型表达式：`Button`、`Edit`、`ComboBox`，或者带约束的 `Button[name="OK"]`
- 键值过滤：`automationId=...`、`name=...`、`class=...`、`className=...`、`helpText=...`、`frameworkId=...`、`type=...`，以及 `isEnabled=true` / `isOffscreen=false` 这种布尔键
- 运算符：`=`（精确）、`~=`（contains）、`^=`（starts with）、`$=`（ends with）、`/=`（正则）
- 索引：`[first]`、`[last]`、`[3]`
- 多段链：用 `>>` 串起来，前一段的结果作为后一段的 scope
- 窗口起手：第一段可以是 `window`，表示从窗口本身开始而不是它的子树

编译结果里有个重要区分：`automationId / name / class / helpText / frameworkId / type / isEnabled / isOffscreen` 这些会被下推成原生 UIA `Condition`（让 COM 层先过滤，快），而 `pid / visible / accessibleName / accessibleRole / localizedType / value` 这些 UIA Condition 表达不了的，会变成「托管过滤」——先用宽松条件取出一批，再用 LINQ 在内存里筛。正则有额外保护：长度上限 200 字符，编译时会拿两个 specially crafted 字符串跑一遍，命中 `RegexMatchTimeoutException` 就判定为灾难性回滚直接拒掉（见 `ValidateRegexPattern`）。编译结果按选择器字符串缓存，上限 2048 条。

真正执行选择器的是 `LocatorEngine`，路径 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Automation\LocatorEngine.cs`。`FindAll(scope, selector)` 编译选择器，然后按段逐层解析：每一段在当前候选集上用 `FindAllDescendants` 配上 Condition 取下一层，再叠加托管过滤和 index。有两处回退值得一提：一是结果数超过 2000 会截断，防 UIA 树爆炸；二是当只剩 name 一个条件且 UIA 一个都找不到时，会回退到 `LegacyAccessibleFallback.FindByAccessibleName`（走 `LegacyIAccessible`），这是为了一些 UIA name 不暴露但 MSAA 暴露的老控件。

`LocatorEngine.FindNearest` 是自愈专用的模糊匹配：给一个目标 automationId 和 name，在 scope 的前 500 个后代里算相似度——automationId 用 Levenshtein 编辑距离（权重 0.6），name 用 Jaccard 分词集合相似度（权重 0.4），取 top N。这套相似度算法在「选择器失效了找最像的元素」时是核心。

### 元素解析、handle、可靠性

`ElementResolver`（`G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Automation\ElementResolver.cs`）是把「handle / selector」解析成真实 `AutomationElement` 的入口。`ResolveOneAsync` 的策略是：优先用 handleId（快），handle 失效（`COMException` / `InvalidOperationException`）时按 handle 里存的 automationId + controlType 重新定位并调 `TryRefreshHandle` 自愈；没有 handle 就用 selector，内部带轮询超时，默认 5 秒每 200ms 试一次；超时还找不到就抛 `NoMatchException`，异常里会带上 `FindNearest` 的 top 5 候选，告诉调用方「你要找的没找到，但这几个长得像」。

它还提供几个前置检查：`EnsureEnabledAsync`（不可用抛 `ElementNotEnabledException`）、`EnsureVisibleAsync`（不可见先试 `ScrollItemPattern.ScrollIntoView` 再等 200ms，还不行抛 `ElementOffscreenException`）、`EnsureStableAsync`（连续采样 bounding box，4 次里要有 2 次位置稳定，否则抛 `ElementUnstableException`——这是为了避免点到正在做布局动画的元素）。

handle 体系在 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Session\HandleRegistry.cs`。handle 就是个 GUID，存 `AutomationElement` 加上它的 `RuntimeId / AutomationId / ControlType / WindowId`，还有 TTL（默认 30 分钟）。满了 1000 个会触发过期清理。`ww_query` 工具里 `createHandle=true` 就会调 `CreateHandle`，后续操作可以直接传 handleId 而不用每次重解析选择器。

可靠性相关的几个类在 `Civyk.WinWright.Core.Reliability\` 下：`WaitFor.ElementStateAsync` 是显式轮询等待（前文 `ww_wait` 用的就是它），`RetryPolicy` 给动作做指数退避重试，`WaitIdle` 等应用进入空闲，`UiaExceptionClassifier` 把一堆 UIA / COM 的 HRESULT 分类成「可重试 / 元素失效 / 永久失败」。

### 动作实现：先语义、后物理

每个交互动作是 `Civyk.WinWright.Core.Actions\` 下一个独立的类，`ClickAction` 是典型。看 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Actions\ClickAction.cs`，它的执行套路是：解析元素 → EnsureEnabled → EnsureVisible → EnsureStable → 然后是个关键策略——**优先走 InvokePattern，回退到物理鼠标**。

具体说，如果是左键单击且没有修饰键，会先试 `PatternFallbackLadder.TryInvoke(element)`，给 2 秒超时；InvokePattern 成功就直接返回，因为这是控件自己的「点击」语义，最干净，不会误伤别的窗口。只有 InvokePattern 不支持（或超时）时，才走真实鼠标：算出元素中心坐标（默认是「左边 +10px、垂直居中」，可用 `offsetX/offsetY` 调），用 `SendInput` 发 `mouse_event` 风格的 down/up，中间穿插修饰键按下 / 抬起。多点之间会释放锁、sleep 50ms 再重新拿锁。所有物理输入走一把全局 `InputLock` 信号量串行化，避免多个 action 并发把键鼠状态搅乱。

这套「先 pattern 后物理」的思路贯穿所有动作。`TypeAction` 会先试 ValuePattern 的 SetValue 再回退到逐字符 SendInput；`SetValueAction`、`SelectAction`、`ExpandCollapseAction`、`SetCheckedAction` 同理。物理回退部分大量用到 `MouseHelper`（SendInput 封装）和 `InputSimulatorStandard`。`PatternFallbackLadder`（`G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Automation\PatternFallbackLadder.cs`）封装了「按 Value → RangeValue → Name → Text 的顺序读值」之类的多级回退，这样同一段代码能适配多种控件。

对话框处理特别值得单独说，见 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Actions\Win32DialogHelper.cs` 和 `Civyk.WinWright.Runner\StepDispatcher.cs` 里的 `HandleDialogReplayAsync` / `HandleMessageBoxReplayAsync`。Win32 的 MessageBox（窗口类 `#32770`）和文件对话框有时 UIA 操作会卡，所以 Winwright 的做法是「双轨」：先在 UIA 线程里试 3 秒（找按钮、按 name 找、调 InvokePattern），超时或者失败就回退到 Win32——用 `FindMessageBoxHwnd` / `EnumWindows` 找到对话框 HWND，直接 `PostMessage` 发 `WM_COMMAND` 的按钮 ID（OK=1, Cancel=2）或者发回车 / ESC 键。文件对话框打开文件还能用 `AutomationId=1148`（文件名输入框）+ `AutomationId=1`（打开按钮）这种硬编码的 known ID。这种「能 UIA 就 UIA，不行就 Win32」的兜底是它能在真实应用上稳定跑的重要原因。

### 快照、标签、状态哈希

`SnapshotEngine`（`G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Snapshot\SnapshotEngine.cs`）是「给模型看的 UI 树」的生产者。它用 BFS 遍历 UIA 树，每个节点产出 `SnapshotNode`，带 `role(=ControlType) / automationId / enabled / visible / bounds / label / value / children`，支持 `depth`（1-30）、`maxElements`（1-2000）截断、`interactiveOnly`（剪掉既不可见又不可用的叶子）、`includeOptions`（对 ComboBox 临时 Expand 取下拉项再 Collapse）。

其中 `label` 字段是这套快照的灵魂，由 `LabelResolver`（同目录）推断。WPF 的很多控件（TextBox、CheckBox）自己没有 Name，光看 UIA 树模型根本不知道这个框是填什么的。`LabelResolver` 用了一套多级策略：

1. 先看 UIA 的 `LabeledBy` 属性（WPF 的 `Label.Target` 会建这个关联），拿到关联 Label 的 Name
2. 没有的话，看父容器的兄弟节点里有没有 Text / Label / TitleBar 类型的，并且空间位置在「左边 50px 内」或「上方 30px 内」——这是按视觉习惯推断「左边那个文字是我的标题」
3. 再不行用 `HelpText`
4. 最后回退到 AutomationId，用 `CamelToTitleCase` 把 `userNameTextBox` 转成 `User Name Text Box`

经过这一套，一个原本身份不明的 Edit 控件在快照里就有了「User Name」这样的可读标签。`ResolveWithSource` 还会返回标签是通过哪种方式关联的（LabeledBy / SpatialLeft / SpatialAbove / HelpText / AutomationId），方便排查。

状态哈希和 diff 在 `StateHasher` 和 `StateDiffer`。`StateHasher` 给整棵可见 UI 树算一个 sha256，`StateDiffer` 比较两个哈希对应的快照，输出 `added / removed / modified` 三类差异。这套机制让 `ww_assert_snapshot` 能做「点了一下之后 UI 应该没变」这种稳定性断言，`ww_diff_state` 能精确告诉模型「这次操作让哪个控件的值变了」。

### 录制：每一步自动落账

录制不是单独的模式，而是内嵌在每次操作里的。`Recorder`（`G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Recording\Recorder.cs`）挂在 session 上，`ww_record` 工具控制它开始 / 停止。一旦处于 active 态，每个动作工具（`ww_click`、`ww_type` 等）执行完都会调 `Recorder.Record(tool, selector, handleId, extra)`，把这一步追加到内存列表里。除非显式传 `record=false`（探查性的步骤不想录进去）。

Recorder 还支持 test case 分段（`StartTestCase` / `EndTestCase`，给步骤打 TestCaseId）、Pop 撤销最后 N 步（`ww_record pop`）、把断言附加到最后一步（`RecordAssertion`）。上限 10000 步防止失控。

导出和解析都在 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Recording\ScriptExporter.cs`。脚本 JSON 的结构是：

```
{
  "version": "1",
  "appId": "...",
  "mode": "test" | "rpa",
  "launchPath" | "attachTitle": "...",
  "defaults": {...},
  "runConfig": {...},
  "testCases": [{id, title, steps}]   // mode=test
  "steps": [...]                       // mode=rpa
}
```

`mode=test` 是结构化的多 test case（CI 跑测试用），`mode=rpa` 是线性的步骤序列（自动化流程用）。每个 step（`RecordedAction`）带 `timestamp / tool / selector / handleId / extra / assertion / fingerprint`，其中 `fingerprint` 存了这一步操作元素的 automationId / name / controlType，是运行时和离线自愈的关键线索。

## Runner：回放和自愈

Runner 这一层把 Core 的能力串成「跑脚本」和「修脚本」两条流程。

回放在 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Runner\Civyk.WinWright.Runner\ScriptRunner.cs`。它拿到 `ParsedScript` 后先看 metadata 里有没有 `launchPath` 或 `attachTitle`，有的话说明脚本要驱动桌面 UI，就开一个 `SessionRegistry` + `SessionOpener` 把目标应用起来 / 附加上；没有的话就是纯浏览器或纯系统脚本，不开桌面 session。然后按 `mode` 分流：test 模式遍历每个 test case，rpa 模式跑线性 steps。

每一步在 `RunStepAsync` 里执行：可选地拍「before」截图 → 调 `StepDispatcher.DispatchAsync` 执行 → 如果 step 带 assertion 就用 `AssertionEvaluator` 算一下 pass/fail，失败再拍「fail」截图 → 返回 `StepResult`（Pass / Fail / Error / Skip）。失败处理由 `ScriptRunConfig` 控制：`ContinueOnFailure` 决定单步失败后是否继续，`MaxFailures` 是全局失败上限，`StepTimeoutMs` 是单步超时，`CaptureScreenshots` / `ScreenshotOnFailureOnly` 控制截图策略。结果可以输出成 text（`TextReporter`）或 JUnit XML（`JUnitReporter`），后者方便接 CI。

`StepDispatcher`（`G:\Projects\Winwright\refsrc\Civyk.WinWright.Runner\Civyk.WinWright.Runner\StepDispatcher.cs`）是分流的中心。它按工具名前缀把 step 路由到不同的执行通道：`ww_browser_*` 走 `IBrowserDispatcher`，`ww_system / ww_process / ww_file / ww_task / ww_service / ww_env / ww_registry / ww_network` 走 `ISystemDispatcher`，`ww_window_*` 和其它桌面 UI 工具走当前 session 的 `WpfSession`。浏览器和系统的 dispatcher 只在脚本确实用到对应工具时才创建（`Program.cs` 里 `source.Any(s => IsBrowserTool(s.Tool))` 判断），避免无谓开销。

桌面工具的分发在 `DispatchAsync(step, session, timeoutMs)` 里，一个大 switch 把 tool 名映射到对应的 `*Action` 类。最关键的是它内置了**运行时自愈**：如果 step 带 `Fingerprint`，先拿原始 selector 试；失败了不直接抛，而是按 `BuildFallbackSelectors` 依次试 `#automationId` → `[name=...][controlType=...]` → `Name="..."`，哪个先成功就用哪个，并在 stderr 打 `[HEALED] ...` 日志。这是一种轻量级的「选择器坏了现场救一下」，不需要离线跑 heal。

离线的、更彻底的自愈在 `G:\Projects\Winwright\refsrc\Civyk.WinWright.Runner\Civyk.WinWright.Runner\ScriptHealer.cs`。它启动目标应用，对脚本里每一个带 selector 的 step：先用 1.5 秒 probe 原选择器，能命中就是 `Ok`；命中不了就 `ExtractHints` 从选择器里抠出 automationId / name 提示，调 `LocatorEngine.FindNearest` 拿 top 5 候选；然后按最高相似度候选（`BuildSelector` 重新构造选择器）分档：≥ `autoHealThreshold`（默认 0.7）标记 `Healed` 并直接替换脚本里的 selector；≥ 0.4 标记 `Suggested` 但不改脚本，等人审；< 0.4 标记 `Unresolvable`。最后输出一份修好的脚本，外加每一步的状态汇总（`ok / healed / suggested / unresolvable` 计数）。

## 浏览器这条副线

虽然 Winwright 主打桌面 UI，但它还内置了一条通过 CDP 驱动 Chromium 的能力，集中在 `Civyk.WinWright.Mcp.Civyk.WinWright.Mcp.Browser\` 命名空间。入口 `BrowserRegistry`（`G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Browser\BrowserRegistry.cs`）的 `ConnectAsync(port, targetUrl)` 流程是：HTTP 打 `http://localhost:{port}/json` 列出所有 page target → 按 `targetUrl` 模糊匹配选一个（没有就取第一个）→ 校验 `WebSocketDebuggerUrl` 的 host 必须是 localhost / 127.0.0.1 / [::1]（防 SSRF，远程地址直接拒）→ 起一个 `CdpClient` 连 WebSocket → 包成 `CdpSession`，自动开对话框监听。后续 `ww_browser_element` / `ww_browser_advanced` 通过这个 session 发 CDP 指令做点击、填值、导航、截图等等。

也就是说，Winwright 同时对标了 playwright（桌面 UI 部分）和 puppeteer / chrome-devtools-mcp（浏览器部分），把两者统一到同一套 MCP 工具命名空间和同一个脚本格式里。脚本里可以混用 `ww_click`（桌面）和 `ww_browser_click`（浏览器），回放时 `StepDispatcher` 自动分流。

`Civyk.WinWright.Mcp.Civyk.WinWright.Mcp.Browser\CssSelectorSanitizer.cs` 和 `JsStringEscaper.cs` 是给浏览器侧做 CSS 选择器消毒和 JS 字符串转义的，防止构造 CDP 调用时注入。

## 配置、权限、审计、远程访问

配置都在 `Civyk.WinWright.Mcp.Civyk.WinWright.Mcp.Config\` 下，根类型 `WinWrightConfig`（`G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Config\WinWrightConfig.cs`）分六块：

`TransportConfig`（端口、绑定地址、TLS 证书、日志级别）、`SecurityConfig`（`AllowedExecutables` 白名单、最大并发 session）、`DefaultsConfig`（handle TTL 分钟数）、`PermissionsConfig`（按工具开关）、`AuditConfig`（审计日志路径和开关）、`RemoteAccessConfig`（`RequireAuthentication`、`IpAllowlist`、`MaxRequestsPerMinute`、`MaxSessionsPerUser`）。配置走 `WinWrightConfigValidator` 校验，启动时失败直接退出。

权限这一块值得展开，因为 Winwright 的工具里有不少是「能改机器」的危险操作（shell、power、registry、file 写……）。`PermissionGuard`（`G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Security\PermissionGuard.cs`）是统一的关卡：`Require(config, toolName, audit, rc)` 拿 `ToolVisibilityFilter.PermissionMap` 查这个工具对应的开关谓词，没开就抛 `permission_denied` 并记审计。`PermissionMap` 是一张「工具名 → 检查 `PermissionsConfig` 哪个字段」的映射表。调用方（比如 `ww_system` 里 shell / power / lock_screen 分支）在执行危险 action 前手动调 `PermissionGuard.Require`。已认证的远程用户还能被 `GroupPermissionOverride` 按组覆盖权限。

审计在 `AuditLogger`，记每次危险工具调用的成功 / 阻断 / 调用者身份。HTTP 模式下还有 `IpAllowlistMiddleware`（IP 白名单）、`GroupAuthorizationMiddleware`（按 Windows 组授权）、按 IP 的固定窗口限流（`MaxRequestsPerMinute`，超了返回 429）。鉴权用 `AddNegotiate` 走 Kerberos / NTLM。

这一整套安全设计在本地 stdio 模式下基本用不上（自己连自己），但体现了 Winwright 的定位：它是可以「让别人远程操作你这台 Windows 机器」的服务端，所以从一开始就把「谁能用哪个工具」「能开几个 session」「从哪个 IP 来」都想清楚了。

## 事件订阅

HTTP 模式下有个独立于 MCP 的 SSE 端点 `/sse/watch`，实现在 `HttpTransportHost.HandleSseWatchAsync`。调用方传 `appId`、可选的 `scope`（一个选择器，限定只看某个子树）和 `events`（事件类型或类别）。服务端在 session 的 `EventWatcher` 上注册，把 UIA 的结构变化、值变化、焦点变化、窗口开关等事件泵成 `EventMessage`，按 filter 过滤后用 SSE 的 `data:` 帧推回去。`ExpandEventCategories` 支持按类别简写：`structure` 展开成 `element_appeared / element_disappeared / structure_changed`，`window` 展开成 `window_opened / window_closed`，等等。这让模型（或外部程序）可以「看着 UI 变化做反应」，而不只是盲操作。

## 怎么构建和运行

构建上有个现实问题要先讲清楚：四个工程的依赖 dll 是用 `<HintPath>` 指到 `G:\Projects\Winwright\bin\` 的，而 `bin\` 看起来是预置好的二进制目录（包含 FlaUI、ModelContextProtocol 等的 dll）。`refsrc` 里没有 NuGet 还原配置（没有 `packages.config` 也没看到 `PackageReference`）。也就是说，正常情况下应该是用某个构建脚本（不在本次分析范围内）先把依赖 dll 准备到 `bin\`，再 `dotnet build G:\Projects\Winwright\refsrc\Solution.sln`。直接在 `refsrc` 里 `dotnet build` 大概率会因为找不到 HintPath 引用的 dll 而失败，除非 `bin\` 已经齐备——而当前 `bin\` 是齐的，里面还能看到 `Civyk.WinWright.Mcp.runtimeconfig.json` 和 `Civyk.WinWright.Mcp.deps.json`，说明这个目录就是 Mcp 工程的运行时输出目录。

运行就是直接跑编出来的 `Civyk.WinWright.Mcp.dll`（或对应的 exe），第一个参数决定子命令：

- 本地调试接 MCP 客户端：`winwright mcp`（stdio）
- 远程或带鉴权：`winwright serve --port 8765`
- 跑测试脚本：`winwright run tests.json --format junit --output result.xml --screenshots`
- 修坏脚本：`winwright heal tests.json --output healed.json --min-confidence 0.7`
- 看某个进程的 UIA 树：`winwright inspect 12345`
- 检查环境：`winwright doctor`

MCP 客户端侧（Claude Desktop、Cursor 等）配置时，stdio 模式把 `winwright mcp` 作为命令即可；HTTP 模式连 `http://localhost:8765/mcp`。

## 几个值得注意的点

最后说几个分析过程中觉得对使用者重要的判断。

第一，**它不止是 UI 自动化工具**。从工具清单能看出来，Winwright 把 Windows 系统管理（进程、服务、注册表、计划任务、文件、网络、电源、shell）也做成了 MCP 工具，野心是「通过 MCP 操作整台 Windows 机器」。做 WPF 调试研究时，UI 相关的工具（lifecycle / query / snapshot / interaction / dialog / wait / assert）是核心，其余系统工具可以当成配套。

第二，**选择器是自研的，不是 CSS / XPath**。模型（或人）写选择器要走 Winwright 自己的 DSL（`#id`、`Type[...]`、`>>` 链、`[first]` 索引等）。这点和浏览器侧的 playwright 不一样，需要在 prompt / 工具描述里讲清楚——好在 `ww_dump_tree` 和 `ww_get_schema` 能帮模型发现可用属性和正确语法。

第三，**稳定性靠多层兜底**。UIA 调用全在专用 STA 线程；元素操作先试 Pattern 再物理输入；选择器失败先在 runtime 用 fingerprint 自愈，离线还能跑 `heal`；对话框先 UIA 后 Win32；handle 会过期但能按 automationId 重定位。这些是它能稳定驱动真实 WPF 应用的底层支撑。

第四，**快照的 label 推断是关键差异化能力**。WPF 控件普遍没 Name，直接 dump UIA 树对模型不友好；`LabelResolver` 通过 LabeledBy / 空间邻近 / HelpText / AutomationId 多级推断，让快照里的每个控件都有可读语义，这是它比「直接暴露 UIA 树」的方案更适合 LLM 驱动的地方。做 WPF 调试研究时，这套 label 推断逻辑值得单独参考。

## 相关源码索引

按模块整理一份关键文件清单，方便跳转：

**入口与传输**
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Program.cs` —— CLI 入口，六个子命令分发
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Transport\HttpTransportHost.cs` —— HTTP/SSE 传输、鉴权、限流、SSE 事件流
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp\ToolHelper.cs` —— 工具结果序列化、异常翻译

**MCP 工具（按职能）**
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Tools\LifecycleTools.cs` —— ww_launch / ww_attach / ww_close / ww_list_windows
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Tools\QueryTools.cs` —— ww_query / ww_dump_tree / ww_get_value / ww_assert
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Tools\SnapshotTools.cs` —— ww_get_snapshot / ww_get_state_hash / ww_diff_state / ww_assert_snapshot
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Tools\InteractionTools.cs` / `ActionTools.cs` / `GridTools.cs` —— 各类交互操作
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Tools\DialogTools.cs` —— 对话框处理
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Tools\SystemTools.cs` —— ww_system（info / shell / power / lock_screen / notification）
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Tools\WaitTools.cs` / `RecordTools.cs` / `InspectTools.cs` —— 等待、录制、元素检视

**Core 引擎**
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Session\WpfSession.cs` —— session 中心
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Session\SessionRegistry.cs` —— session 工厂与并发管理
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Threading\UiaDispatchThread.cs` —— STA 调度线程
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Session\FrameworkDetector.cs` —— WPF / WinForms / Win32 识别
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Session\HandleRegistry.cs` —— 元素 handle 与 TTL
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Automation\LocatorEngine.cs` —— 选择器执行与模糊匹配
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Automation\ElementResolver.cs` —— handle/selector 解析、stale 自愈、前置检查
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Actions\ClickAction.cs` —— 动作实现样板（先 Pattern 后物理）
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Actions\Win32DialogHelper.cs` —— 对话框 Win32 兜底
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Snapshot\SnapshotEngine.cs` —— 快照生产
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Snapshot\LabelResolver.cs` —— 标签多级推断
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Reliability\WaitFor.cs` —— 显式等待
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Recording\Recorder.cs` —— 录制器
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Core\Civyk.WinWright.Core.Recording\ScriptExporter.cs` —— 脚本 JSON 读写

**选择器 DSL**
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Selectors\Civyk.WinWright.Selectors.Parsing\SelectorParser.cs` —— 语法分析
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Selectors\Civyk.WinWright.Selectors.Ast\SelectorNode.cs` —— AST 节点
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Selectors\Civyk.WinWright.Selectors.Execution\SelectorCompiler.cs` —— 编译器与缓存

**Runner**
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Runner\Civyk.WinWright.Runner\ScriptRunner.cs` —— 脚本回放
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Runner\Civyk.WinWright.Runner\StepDispatcher.cs` —— 工具分流与运行时自愈
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Runner\Civyk.WinWright.Runner\ScriptHealer.cs` —— 离线自愈

**浏览器副线**
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Browser\BrowserRegistry.cs` —— CDP 连接管理
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Browser\CdpSession.cs` —— 单个浏览器会话

**配置与安全**
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Config\WinWrightConfig.cs` —— 配置根
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Security\PermissionGuard.cs` —— 权限关卡
- `G:\Projects\Winwright\refsrc\Civyk.WinWright.Mcp\Civyk.WinWright.Mcp.Security\AuditLogger.cs` —— 审计日志
