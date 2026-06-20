# 设计方案（草稿）：McpServerForDevEnv 的 WPF 调试机制

这是「给 McpServerForDevEnv 加 WPF 调试能力」的初步设计，配套 `req-analysis.md` 一起看。需求拆解和概念映射在那份里，这份专注怎么实现：架构怎么分、项目怎么摆、概念怎么建模、IPC 怎么走、工具集怎么设计、vbnet 脚本怎么跑、怎么接进现有主控。最后单开一章讲还没把握的技术困难。

风格上对标 chrome-devtools-mcp 的能力面，但全程围绕「被控进程 + 窗口」而不是浏览器 page。参考了 `G:\Projects\McpServerForDevEnv\Research\WpfDebugging\docs\` 下的分析文档，以及 agent 摸出的 McpServerForDevEnv 现状基线。

## 一、总体架构

整套机制分三层，外加一条 IPC 链路：

```
外部 AI ──HTTP/MCP──> 主控 McpServerForDevEnv ──named pipe──> 被控 WPF 进程（内嵌 NuGet 包）
        (现有)              │  (协议网关)                          (能力提供者，在 UI 线程干活)
                            │
                            └─ 共享 Core（契约库，两边都引用）
```

- **被控端**（新）：一个多目标（net472;net8.0-windows，UseWpf=true）的 NuGet 类库，WPF 程序引用它即获得调试能力。它在宿主进程内拉起 named pipe server，在 UI 线程上执行所有 WPF 操作、跑 vbnet 脚本、维护元素 uid 映射。它是「能力提供者」。
- **主控**（现有 McpServerForDevEnv 扩展）：扮演协议网关。外部 AI 用 HTTP MCP 调工具，主控把调用翻译成 pipe 命令发给被控端，结果包成 MCP 响应回传。同时管连接（新 tab）、管工具注册。
- **共享 Core**（新）：两边都要用的契约——IPC 消息类型、快照数据模型、元素引用约定。主控和被控端都引用它。
- **named pipe**：主控↔被控端的内部协议，不是 MCP，是「WPF 调试命令」。

和 chrome-devtools-mcp 的关键区别：chrome-devtools-mcp 工具是同进程直接操作 page（puppeteer 在 server 进程内）；这里工具必须跨进程 IPC 委托给被控端，因为 WPF 元素只活在被控进程里。这决定了「元素引用靠 uid 字符串、操作靠被控端代劳」的整体形态。

## 二、项目结构与 Core 抽取

现有解决方案（`G:\Projects\McpServerForDevEnv\McpServiceNetFx.sln`）是 VB.NET net472，有 `McpServiceNetFx`（主宿主 GUI）、`McpServiceNetFx.Core`（公共库，主控专用，依赖 EnvDTE）、`McpServiceNetFx.VsixAsync`（VSIX 平行宿主）、`McpServiceNetFx.Tests`。新增项目建议：

1. **共享契约库**（新）。多目标 `net472;net8.0-windows`，纯 POCO + 接口，零 EnvDTE 依赖、零 WPF 强依赖（顶多用 `DependencyObject` 之类的类型时注意目标兼容）。承载 IPC 消息、快照模型、元素引用、工具协议接口。建议命名 `McpServerForDevEnv.WpfDebugging.Core`。**这是用户要的「抽取 Core」**——之所以不塞进现有 `McpServiceNetFx.Core`，是因为后者单目标 net472 且依赖 EnvDTE，被控端包既不能多目标兼容、也不该背 EnvDTE 这个包袱。语言上可以沿用 VB.NET（和主控一致），也可以用 C#（被控端生态更熟），这点列为开放项。
2. **被控端 NuGet 包**（新）。多目标 `net472;net8.0-windows`，`UseWpf=true`。引用共享 Core。实现 pipe server、可视树快照、uid 映射、vbnet 脚本引擎、截图、事件采集。产出 NuGet 包供被控 WPF 程序引用。命名 `McpServerForDevEnv.WpfDebugging.Target`。
3. **主控侧 WPF 调试模块**（在现有 `McpServiceNetFx` + `McpServiceNetFx.Core` 里新增）。net472。引用共享 Core。包括 WPF 调试 tab（`MainWindow.WpfDebug.vb`）、pipe client、被控端代理、连接管理、一批 WPF 调试工具类、工具 DC 注入扩展。

依赖关系最终是：

```
McpServiceNetFx ──> McpServiceNetFx.Core ──> (现有 VS 逻辑)
      │                     │
      └──────────> McpServerForDevEnv.WpfDebugging.Core <──── McpServerForDevEnv.WpfDebugging.Target (NuGet)
                     (共享契约，多目标)
```

共享 Core 作为被控端包的依赖，要么一起发 NuGet，要么被控端包用源码内联（拼成一个包）——倾向单独发 Core 包，被控端包依赖它，主控也引用它，三方共享同一套契约类型。

## 三、概念模型

对齐 chrome-devtools 但换成 WPF：

- **Target（被控进程）**：一个跑起来、内嵌了调试包的 WPF 应用实例。对应 chrome 的 browser 进程。主控维护「当前 Target」。
- **Window（窗口）**：被控进程里的一个顶级窗口（`System.Windows.Window` 或主 `HwndSource` 根）。**这是主要操作对象**，对应 chrome 的 page。一个 Target 可有多个 Window，AI 通常锁定一个操作。
- **Element（元素）**：可视树里的一个 `DependencyObject`（多为 `FrameworkElement`）。对应 chrome 的 DOM/a11y 节点。
- **uid（元素稳定引用）**：跨快照稳定的字符串 id，主控和 AI 只持有 uid，被控端维护 uid↔对象映射。对应 chrome 的 snapshot uid（见 `take-snapshot-a11y快照格式.md` 的 uid 规则）。

主控对外的工具参数里，「目标」用 `windowId`（可选，默认当前 window）+ `uid`（元素引用），结构上对标 chrome 的 `pageId` + `uid`，但顶层是 window 不是 page。

## 四、被控端 NuGet 包设计

被控端是能力核心。要点：

1. **启动入口**：暴露一个 `WpfDebugHost.Start(config)` 静态方法，宿主程序在 `Application.Startup`（或 `App.xaml.cs`）里调用一次；也提供一个 `WpfDebugModule` 让宿主选择自动挂载。`config` 含是否启用脚本、权限提示回调等（pipe 名写死成固定值，不由 config 指定）。包本身不强迫宿主改架构。
2. **pipe server**：`Start` 后用一个后台 `NamedPipeServerStream` 监听循环（一个连接一个任务）。pipe 名**写死成固定值，并带协议版本号**（如 `mcpserverfordevenv.wpfdebug.v1`），避免和别的版本/用途串扰——单被控端语义下同一时刻只允许一个被控端占用这个名字，第二个想起的会撞名，直接报错即可。net472 下 `NamedPipeServerStream` + async 完全可用，无新依赖。
3. **UI 线程调度**：所有碰 WPF 对象的命令，经 `Application.Current.Dispatcher.InvokeAsync` 切到 UI 线程执行。IPC 线程只做收发和序列化。这是 STA 安全的关键（和主控现有 `IDispatcher` 模式同源）。
4. **uid 映射**：用 `ConditionalWeakTable(Of DependencyObject, String)` 把对象附着上 uid，外加一个 `Dictionary(Of String, WeakReference(Of DependencyObject))` 反查。拍快照时遍历可视树分配/复用 uid，按 uid 取回元素时查反查表（取不到或已 GC 就报「元素失效」）。这套在 net472 和 net8.0 上都成立。
5. **能力实现**：可视树遍历（含 AdornerLayer/Popup，参考 `WPFVisualTreeMcp-分析.md`）、依赖属性读写、绑定检视、截图（`RenderTargetBitmap`）、vbnet 脚本（见第八节）、事件采集（`DispatcherUnhandledException`、`PresentationTraceSources` 绑定错误、`Trace` 监听、窗口 `Opened`/`Closed`）。
6. **优雅退出**：宿主 `Exit` 时关 pipe、清理映射、停监控。

## 五、IPC 协议

主控↔被控端的内部协议，不是 MCP。设计为 JSON-RPC 风格 over named pipe，length-prefixed 帧（4 字节大端长度 + UTF-8 JSON），复用 Newtonsoft 序列化（net472/net8 都能用，保持一致）。主控连固定的 pipe 名，连上即发现被控端；握手时被控端回报自己的进程信息（pid、主窗口标题、协议版本）。共享 Core 里定义消息类型：

- 请求 `WpfDebugRequest { Id, Method, Params }`；响应 `WpfDebugResponse { Id, Result, Error }`；事件 `WpfDebugEvent { Event, Data }`（被控端主动推）。
- Method 是 WPF 调试命令：`list_windows`、`take_snapshot`、`click`、`fill`、`evaluate`、`take_screenshot`、`get_property`、`set_property`、`get_logical_tree`、`list_bindings`、`highlight`、`list_events` 等。
- 大对象：截图回传用 base64 包进 JSON（先简单），超大可视树走分页（`windowId` + `afterUid` + `maxDepth`，对标 chrome 的 pagination 思路，见 `G:\Projects\chrome-devtools-mcp\src\utils\pagination.ts`）。

主控侧 pipe client（`WpfDebugProxy`）封装连接、发请求、收响应、订阅事件缓存，给工具当 DC 用。被控端侧 pipe server 收到 `WpfDebugRequest` → 查方法表 → 在 UI 线程执行 → 回 `WpfDebugResponse`。

事件流（异常/绑定错误/Trace/窗口开关）由被控端发 `WpfDebugEvent`，主控 client 缓存，供 `list_events` 工具读（对标 chrome 的 console messages 模型）。

## 六、Visual tree snapshot 与稳定 uid

这是对 chrome `take_snapshot` 的 WPF 版，也是 AI 操作的基石。设计要点：

- **快照对象**：指定 `windowId` 的可视树（`VisualTreeHelper` 遍历），可选 `maxDepth`、可选 `interestingOnly`（裁掉纯装饰节点，对标 chrome 的 `interestingOnly`，见 `take-snapshot-a11y快照格式.md`）。
- **文本格式**：对标 a11y snapshot 的「缩进树 + 每节点一行」：`uid=<id> <控件类型> "<名称>" <属性...>`。缩进每层 2 空格；控件类型取 `GetType().Name`（如 `Button`、`TextBox`）；名称取 `x:Name` 或 `Name` 或 `Content`/`Text` 摘要；属性挑有调试价值的：`IsEnabled`、`Visibility`、`IsVisible`、`IsFocused`、`IsHitTestVisible`、绑定摘要等。布尔属性直接印 key，标量印 `key="value"`，规则照搬 a11y 那套（见 `G:\Projects\chrome-devtools-mcp\src\formatters\SnapshotFormatter.ts`）。
- **结构化形态**：同时产一份树形 JSON（节点含 uid/类型/名称/属性/子节点），进 MCP `structuredContent`，对标 chrome 的 `snapshot.toJSON()`。
- **uid 稳定性**：第四节那套 `ConditionalWeakTable` 方案——同一对象跨快照同 uid。被控端持有映射，主控只拿 uid 字符串。
- **失效处理**：操作时若 uid 对应对象已被 GC 或不在当前可视树，返回明确错误，提示重新 snapshot（对标 chrome 元素失效的处理）。

## 七、工具集设计

工具继承现有 `VisualStudioToolBase`（`G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\VisualStudioToolBase.vb`）的模板方法模式，但内部不调 `_vsTools`（VS/DTE），改调 `_wpfDebugProxy`（被控端代理）。建议加一个 `WpfDebugToolBase` 中间基类，统一持有 `_wpfDebugProxy` 和 `IsWpfDebugConnected` 判断，区分 VS 工具和 WPF 工具。

工具清单（对应 `req-analysis.md` 第四节）按批次实现，每个工具的 schema 手填 JSON Schema（沿用现有 `ToolDefinition` 写法，见 `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\GetSolutionInfoTool.vb`）。MVP 六个：

- `list_windows`：无参（或 `targetId`），返回窗口列表（windowId/标题/是否主窗口/句柄）。
- `take_snapshot`：参 `windowId?`、`maxDepth?`、`interestingOnly?`、`filePath?`，返回可视树文本 + 结构化。
- `click`：参 `uid`，点击元素（优先 `AutomationPeer.Invoke`，退化到 `RaiseEvent`）。
- `fill`：参 `uid`、`value`，按控件类型分流：`TextBox.Text`、`ComboBox.SelectedValue`、`CheckBox.IsChecked`、`RadioButton.IsChecked`。
- `evaluate`：参 `script`(VB.NET)、`timeout?`，返回脚本结果。
- `take_screenshot`：参 `windowId?`/`uid?`、`filePath?`，返回图像。

工具注册沿用 `VisualStudioToolManager` 的 `RegisterTool`/`UnregisterTool`（`G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\VisualStudioToolManager.vb`），现有 API 已支持运行时增删。

## 八、VB.NET 脚本执行

`evaluate` 工具的内核。参考 `VBScriptDotNet-编译执行与程序集加载.md` 的成熟做法：

- 用 Roslyn 的 VB.NET 脚本编译 API（`Microsoft.CodeAnalysis.VisualBasic`），脚本源 → 脚本编译 → 内存 PE → 加载 → 反射执行入口点，拿到返回值序列化回传。VBScriptDotNet 已验证这条链路（`Vbi.Main` → `VisualBasicScript.RunInteractive` → Roslyn `CreateScriptCompilation` → `Emit` → `InteractiveAssemblyLoader`）。
- **脚本上下文注入**：执行前构造一个宿主对象传给脚本，暴露 `Application`（= `Application.Current`）、`MainWindow`/`ActiveWindow`、`Element(uid)`（按 uid 取元素，自动切 UI 线程）、`SetProperty`/`GetProperty` 辅助、`Log`。AI 写的脚本就像在被控程序里写代码。
- **引用程序集**：以被控进程已加载的程序集作为脚本引用（遍历 `AppDomain.GetAssemblies()` / `AssemblyLoadContext`），让脚本能直接 `Imports` 宿主的命名空间、用宿主类型。
- **程序集加载（双目标核心差异）**：脚本编译产物的加载，net472 走 AppDomain + `Assembly.Load`（参考 VBScriptDotNet 的 `DesktopAssemblyLoaderImpl` + `AssemblyResolve`），net8.0 走 `AssemblyLoadContext`（参考 `CoreAssemblyLoaderImpl` + `Resolving`）。可以直接参考 VBScriptDotNet 那套运行时探测分流（`CoreClrShim.AssemblyLoadContext.Type` 是否拿得到来决定走哪条），别大量用条件编译。
- **UI 线程**：整个 evaluate（编译 + 执行）调度到被控进程 UI 线程上跑，脚本里碰元素天然安全。

## 九、主控集成

照搬现有模式，改动点明确：

1. **WPF 调试 tab**：`G:\Projects\McpServerForDevEnv\McpServiceNetFx\Views\MainWindow.xaml` 的 `TabControlMain` 加一个 TabItem；新建 `MainWindow.WpfDebug.vb`（partial），仿 `.VsInstances.vb` 的拆法。单被控端 + 固定 pipe 名，UI 很简单：一个「连接/断开」按钮 + 当前连接状态（握手回来的 pid、主窗口标题），不需要连接列表、不需要让用户填地址。
2. **连接管理**：固定 pipe 名、单被控端，不需要多连接列表和持久化。一个 `WpfDebugConnection`（存握手回来的 pid、主窗口标题、协议版本）+ 一个连接/断开动作 + `WpfDebugConnectionMonitor`（照搬 `VisualStudioMonitor` 的判活/失效事件/清理，判活依据改为 pipe 连接状态 + 目标 pid 存活，见 `G:\Projects\McpServerForDevEnv\McpServiceNetFx\Helpers\VisualStudioMonitor.vb`）。
3. **被控端代理 + DC 注入**：`WpfDebugProxy`（pipe client）当数据上下文。照搬现有「选中 → `CreateVsTools` → `SetVsTools`」模式：`VisualStudioToolManager` 加 `CreateWpfDebugTools(proxy, dispatcher)`，遍历 WPF 工具调 `SetWpfDebugProxy(proxy)`。`VisualStudioToolBase` 加 `_wpfDebugProxy` 字段和 `SetWpfDebugProxy`。
4. **工具可见性**：WPF 工具常驻注册，不追求「连上才出现」。没连上被控端时，调用按现有 VS 工具「未初始化」的风格报「未连接被控端」。不实现 `listChanged` 推送，不动现有 `listChanged=False`。
5. **VSIX**：不支持，只在独立 GUI 版（`McpServiceNetFx`）实现。

## 十、分阶段实施

- **阶段一（MVP 单被控端闭环）**：共享 Core + 被控端包（先 net472，net8.0 紧随）+ 主控 WPF 调试 tab + pipe 通道 + MVP 六工具（list_windows/take_snapshot/click/fill/evaluate/screenshot）。手动填 pipe 名连接。单被控端。
- **阶段二（WPF 特色 + 事件）**：依赖属性读写、逻辑树、绑定调试、highlight、事件流工具。net8.0 被控端补齐并验证双目标一致。
- **阶段三（完善与稳健性）**：事件流工具、绑定错误/逻辑树等 WPF 特色能力补齐、net8.0 被控端的双目标一致性验证收尾、uid 与大对象分页在真实大型应用上调优。（多被控端路由不做——已定单被控端；发现机制已由固定 pipe 名解决。）

## 十一、技术困难与未决问题

下面是设计里我**还没完全把握、需要进一步验证或拍板**的点。这些是真正决定方案能不能落地的关键，单列出来。

### 1. 跨进程的元素稳定引用（uid）

这是最核心也最没现成答案的点。chrome 靠 CDP 的 `backendNodeId`（浏览器侧维护），WPF 没有等价物。我的设想是被控端进程内用 `ConditionalWeakTable(Of DependencyObject, String)` 把对象附着 uid、反查表按 uid 取回——理论上能保证「同一对象跨快照同 uid、对象 GC 后自动失效」。**但还没验证**：net472 和 net8.0 下 `ConditionalWeakTable` 的行为是否完全一致（尤其对象被移出可视树但还被别处引用、或弱引用回收时机的边界）；大型应用里这张表的内存与清理开销；以及「元素被重建」（列表刷新生成新对象）时 uid 必然变化，AI 操作循环里要不要有「失效即重拍」的协议约定。这块需要在 MVP 阶段写原型验证，是头号风险。

### 2. 被控端双目标（net472 vs net8.0-windows）的 API 分歧

被控包要同时跑两个运行时，差异主要在两处：脚本编译产物的程序集加载（AppDomain/`AssemblyResolve` vs `AssemblyLoadContext`/`Resolving`），以及少量 WPF/反射 API 的细微差别。VBScriptDotNet 用「运行时反射探测 + 极少条件编译」解决过同类问题（见 `VBScriptDotNet-编译执行与程序集加载.md`），可以照搬思路，**但 VBScriptDotNet 的目标是 `netstandard2.0;net10.0`，不是 `net472;net8.0-windows`，不能直接套用**，得重新核对每个分支点在 net472/net8.0-windows 下的可用性。共享 Core 本身多目标也会放大这类分歧。

### 3. Roslyn 脚本引擎的体积与启动成本

`Microsoft.CodeAnalysis.VisualBasic` 体积很大（几十 MB 量级），被控 WPF 程序引用调试包后，发布体积和启动内存会明显增加；首次 `evaluate` 的脚本编译也有几百毫秒到秒级延迟。**没把握的点**：能不能把 Roslyn 做成可选/懒加载（只在第一次 evaluate 时加载，或拆成独立卫星包），让不需要脚本的被控端不背这个成本。这影响被控端包的分发形态（单包 vs 多包）。

### 4. UI 线程/STA 调度与死锁

被控端所有 WPF 操作必须经 `Dispatcher.InvokeAsync` 切 UI 线程。如果某条命令在 IPC 线程上**同步等待** UI 线程的结果（`Invoke` 而非 `InvokeAsync` + await），而 UI 线程此刻被卡住（比如宿主自己跑了个模态对话框、或在 Dispatcher 上排队了长任务），就会死锁。**设计约束**：被控端必须全程异步（`InvokeAsync` + `Await`），绝不阻塞等 UI 线程；同时要约定「宿主 UI 线程长时间不可用时，调试命令超时报错」而不是挂死。vbnet 脚本里如果 AI 写了同步阻塞 UI 的代码也会触发同类问题，难完全防御。

### 5. IPC 大对象与序列化

截图（PNG，可能几 MB）、大型可视树（复杂应用单窗口上千节点）都要序列化过 pipe。base64 进 JSON 能跑但低效且占内存；超大可视树全量回传会让单次 snapshot 又慢又大。**方向**是 snapshot 默认裁剪 + 分页（`maxDepth`/`afterUid`，对标 chrome 的 pagination），截图考虑直接 length-prefixed 二进制帧而非 base64 进 JSON。具体阈值和分页协议要试出来的。

### 6. 被控端 pipe server 的启动时机与生命周期

被控包要在宿主进程里起 pipe server，**没把握的是入口约定**：是要求宿主在 `Application.Startup` 显式调 `WpfDebugHost.Start`（侵入小但要宿主配合），还是包靠模块初始化器（`Microsoft.Xaml.Behaviors` 或类似）自动挂载（无侵入但隐式、难调试）。net472 和 net8.0 的模块初始化机制还不一样。另外宿主多实例、宿主是 ClickOnce/MSIX 打包等场景下 pipe 权限会不会出问题，要验证。**还有固定 pipe 名 + 单被控端带来的占用冲突**：本机已有一个被控端占用了这个固定名时，第二个被控进程起 server 会撞名——单被控语义下应直接报错提示「已有调试会话」，而不是排队或换名。

### 7. VB.NET 脚本的宿主 API 形状与权限提示

脚本权限已定为完全放开（等同被控进程权限，见 `req-analysis.md` 第八节），所以难点不在沙箱，而在**宿主 API 的形状**：`Element(uid)` 这种注入函数怎么把「按 uid 取元素 + 自动切 UI 线程」封装得让 AI 写脚本时直觉正确、又不暴露过多内部；`Application`/`ActiveWindow`/`SetProperty`/`GetProperty` 等辅助怎么排才好用。另外 `evaluate` 等于全权，权限提示（现有 `IMcpPermissionHandler`，见 `VisualStudioToolBase`）怎么写才不流于形式——现有权限模型是面向文件/VS 的，WPF 操作（改属性、点击、跑脚本、截图）的粒度（按工具放行 vs 区分只读/改写）还要定。

## 十二、相关参考（绝对路径）

**研究文档（同方案目录上层）：**
- `G:\Projects\McpServerForDevEnv\Research\WpfDebugging\docs\chrome-devtools-mcp-工具定义说明.md` — 工具定义/注册/执行流程，工具集设计对标。
- `G:\Projects\McpServerForDevEnv\Research\WpfDebugging\docs\take-snapshot-a11y快照格式.md` — snapshot 文本格式与 uid 规则，可视树快照对标。
- `G:\Projects\McpServerForDevEnv\Research\WpfDebugging\docs\McpServerForDevEnv-MCP通信与DTE实例管理.md` — 主控现状，集成点依据。
- `G:\Projects\McpServerForDevEnv\Research\WpfDebugging\docs\WPFVisualTreeMcp-分析.md` — 可视树遍历（含 Adorner/Popup）、截图、注入式做法（本方案不用注入，但树遍历/截图可借鉴）。
- `G:\Projects\McpServerForDevEnv\Research\WpfDebugging\docs\Winwright-分析.md` — 桌面 UI 自动化的工具组织、选择器思路（参考其工具分层，不强用其 DSL）。
- `G:\Projects\McpServerForDevEnv\Research\WpfDebugging\docs\VBScriptDotNet-编译执行与程序集加载.md` — vbnet 脚本引擎与双运行时程序集加载，第八节直接参考。

**主控现有源码（集成点）：**
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx\Views\MainWindow.xaml` — `TabControlMain`，加 WPF 调试 tab 的位置。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx\Helpers\VisualStudioMonitor.vb` — 连接监控模式，被控端连接监控照搬。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\VisualStudioToolBase.vb` — 工具基类模板方法。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\VisualStudioToolManager.vb` — 工具注册/DC 注入，扩展点。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Mcp\VisualStudioMcpHttpService.vb` — 协议处理器，pipe 通道复用其请求处理思路。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Helpers\Abstractions.vb` — `IDispatcher` 等可复用抽象。

**chrome-devtools-mcp 源码（能力对标）：**
- `G:\Projects\chrome-devtools-mcp\src\tools\snapshot.ts`、`TextSnapshot.ts`、`formatters\SnapshotFormatter.ts` — snapshot 与 uid。
- `G:\Projects\chrome-devtools-mcp\src\utils\pagination.ts` — 分页思路。
- `G:\Projects\chrome-devtools-mcp\src\tools\input.ts` — 元素操作工具范式。

已定决策见 `req-analysis.md` 第八节；本章列出的是仍需验证或设计的剩余技术风险（共 7 条）。
