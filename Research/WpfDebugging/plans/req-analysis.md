# 需求分析：为 McpServerForDevEnv 增加 WPF 调试机制

这份文档把「给 McpServerForDevEnv 加一套 WPF 调试能力」的需求拆开讲清楚：原始诉求是什么、每一句该怎么理解、和 Chrome DevTools 概念怎么对应、要做哪些功能、哪些不做、和现有系统怎么衔接。技术上的难点和没把握的地方不放在这里，统一放到 `rough-plan.md` 的「技术困难与未决问题」一章。

写作时参考了 `G:\Projects\McpServerForDevEnv\Research\WpfDebugging\docs\` 下的几篇分析，尤其是 `chrome-devtools-mcp-工具定义说明.md`、`take-snapshot-a11y快照格式.md`、`McpServerForDevEnv-MCP通信与DTE实例管理.md`、`WPFVisualTreeMcp-分析.md`、`Winwright-分析.md`、`VBScriptDotNet-编译执行与程序集加载.md`。

## 一、原始需求

把用户提的诉求原样列出来，逐条解读：

1. **功能尽量像 Chrome DevTools**，但把「page（页面/标签页）」的概念弱化，换成以「window（窗口）」为主的概念。
2. 远程执行脚本的能力支持 **VB.NET**，而不是 JavaScript。
3. **不支持操作 Chrome 浏览器**，专注于操作被控进程。
4. **被控端**：WPF 程序内嵌一个 NuGet 依赖来获得调试能力。这个 NuGet 包由一个类库项目产生，多目标 `net472` 和 `net8.0-windows`（且 `UseWpf=true`），用来提供和 chrome-devtools-mcp 类似的能力。
5. **主控程序**是 McpServerForDevEnv，和被控端之间用 **named pipe** 通信。
6. 新增一个 **WPF 调试 tab**，用来管理与被控端的连接（方式上类似于现在管理 devenv 连接）。
7. **扩展 MCP 工具集**，让 WPF 调试工具也出现在对外的工具列表里。
8. 公共数据结构和机制**抽取到 Core 项目**。

## 二、逐条解读与澄清

**关于「像 Chrome DevTools，但以 window 为主」。** Chrome DevTools / chrome-devtools-mcp 的对象层次是 浏览器 → 标签页(page) → DOM/无障碍树 → 元素。WPF 这边对应的层次是：被控进程（一个跑起来的 WPF 应用）→ 窗口(`Window` / 主窗口，一个进程可能有多个）→ 可视树/逻辑树 → `FrameworkElement`。用户要把 page 弱化、window 抬上来，意思是：**主要操作对象是窗口**，AI 调试时先选/列窗口，再在某个窗口里拍快照、找元素、操作元素。所以这套调试机制的顶层概念不是「当前 page」，而是「当前被控进程 + 当前窗口」。一个被控进程里可以并列存在多个窗口。

**关于「VB.NET 而非 JavaScript」。** Chrome DevTools 里 `evaluate` 系工具能在页面上下文里跑 JS。这里的对应物是：在被控进程的上下文里跑一段 VB.NET 脚本，能拿到 `Application.Current`、当前窗口、按 uid 引用的元素等，用来读属性、改状态、触发逻辑。这是一个很强的调试手段，也是和 chrome-devtools-mcp 的核心差异点之一。技术上要参考 `VBScriptDotNet-编译执行与程序集加载.md` 里那套 Roslyn 脚本编译的做法。

**关于「专注被控进程、不碰 Chrome」。** 明确排除浏览器自动化。这意味着 chrome-devtools-mcp 里和浏览器强相关的工具（导航 navigate、网络 network、Cookie、扩展、performance trace、heap snapshot 等）要么不做，要么换成 WPF 语境下的等价物（比如「网络」没有对应，「控制台」对应被控进程的 Trace/Debug 输出和未处理异常）。

**关于「被控端是内嵌 NuGet 包，不是远程注入」。** 这点要特别强调，因为它决定了整个架构的复杂度。`WPFVisualTreeMcp-分析.md` 里那个项目是靠远程注入（`CreateRemoteThread` + 原生 bootstrapper 拉 CLR）进目标进程的，复杂且有风险。本需求明确是**被控 WPF 程序主动引用一个 NuGet 包**，包在进程内就地提供调试能力——不需要注入、不需要native bootstrapper。这大大简化了被控端，代价是被控程序必须愿意（也允许）在工程里加这个依赖。

**关于「被控端包多目标 net472 和 net8.0-windows」。** 被控 WPF 程序可能是老 Framework（net472）也可能是新 .NET（net8.0-windows）。调试包两个目标都要覆盖。这会带来 API 差异（程序集加载、部分反射/WPF API），处理方式可以借鉴 VBScriptDotNet「运行时探测 + 少量条件编译」的思路（见 `VBScriptDotNet-编译执行与程序集加载.md`）。

**关于「主控用 named pipe 和被控端通信」。** 主控 McpServerForDevEnv 现在对外是 HTTP MCP（见 `McpServerForDevEnv-MCP通信与DTE实例管理.md`），对被控 VS 是跨进程 COM（DTE）。对被控 WPF 程序这条新链路用 named pipe。也就是说主控在协议上扮演「网关」：外部 AI 用 HTTP MCP 调工具 → 主控把工具调用翻译成 pipe 命令发给被控端 → 被控端在 UI 线程上执行 → 结果回传 → 主控包成 MCP 响应。

**关于「新增 WPF 调试 tab、像管 devenv 连接那样管被控端」。** 现在主控 MainWindow 的 `TabControlMain`（`G:\Projects\McpServerForDevEnv\McpServiceNetFx\Views\MainWindow.xaml`）里已有「服务管理」等 tab，用 DataGrid 枚举本机 VS 实例、选中一个作为当前目标。WPF 调试 tab 要复用这套「tab + 选中当前目标」的模式，但被控端用固定的 pipe 名、且是单被控端，所以 UI 比 VS 那套更简单：连固定名、连上即当前目标，一个连接/断开按钮加状态显示就够，不用枚举、不用添加/移除多个连接。

**关于「扩展工具集、WPF 工具出现在工具列表」。** 现有工具手动注册进 `VisualStudioToolManager` 的 `ConcurrentDictionary`，且 `listChanged=False`、工具一旦注册就一直列在 `tools/list`（见 `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\VisualStudioToolManager.vb`）。WPF 调试工具要纳入这套注册机制；同时希望它们在没连上被控端时不要误导 AI——要么连上才出现，要么列得出但调用时给出「未连接」的明确提示。

**关于「公共数据结构和机制抽取 Core」。** 现在已经有一个 `McpServiceNetFx.Core`（net472，主控专用，含 EnvDTE 依赖）。被控端包是双目标、不能依赖 EnvDTE，所以公共部分不能直接塞进现有 Core。需要确立一个**被主控和被控端共享的 Core**，承载两边都要用的契约：IPC 消息类型、快照数据模型、元素引用约定、工具接口等。具体怎么和现有 Core 共存，见 `rough-plan.md`。

## 三、概念映射：从 Chrome DevTools 到 WPF

把 chrome-devtools-mcp 的核心概念逐个映射到 WPF 调试语境，这是工具集设计的依据：

- **浏览器进程 / page（标签页）** → **被控进程 / window（窗口）**。page 弱化、window 为主；一个被控进程可有多个 window，AI 通常锁定一个 window 操作。
- **DOM / 无障碍树（a11y tree）** → **可视树（visual tree）为主，逻辑树（logical tree）为辅**。WPF 的可视树对应渲染结构，最接近 DOM/a11y 树的语义；逻辑树是 WPF 特有的另一套，调试时也需要。
- **元素 + 稳定引用（backendNodeId / uid）** → **元素 + 稳定 uid**。chrome-devtools 靠 `TextSnapshot.ts` 里的 `loaderId_backendNodeId` 给节点编稳定 uid（见 `take-snapshot-a11y快照格式.md`）。WPF 元素没有原生稳定 id，但被控端在进程内、可以维护「对象→uid」的映射来达到同样效果（具体方案见 plan）。
- **`take_snapshot`（拍 a11y 树文本快照）** → **拍可视树文本快照**，格式对标 a11y 那套（缩进树 + `uid=` + 类型 + 名称 + 属性），但属性换成 WPF 的：控件类型、`Name`/`x:Name`、`Content`、`IsEnabled`/`IsVisible`/`IsFocused`、`Visibility`、绑定到的属性名等。
- **`evaluate`（JS）** → **`evaluate`（VB.NET 脚本）**，在被控进程上下文执行。
- **`click`/`fill`/`hover`/`press_key`（输入）** → 对应 WPF 元素操作：点击按钮（走 `AutomationPeer` 或直接触发）、填 `TextBox`、选 `ComboBox`、移动焦点等。注意 WPF 没有 web 那样统一的「填表」语义，要按控件类型分流。
- **`take_screenshot`** → 渲染窗口/元素为位图（`RenderTargetBitmap`），参考 `WPFVisualTreeMcp-分析.md` 的做法。
- **console messages** → 被控进程的 `Trace`/`Debug` 输出、`PresentationTraceSources` 的绑定错误、未处理异常。
- **network / performance trace / heap snapshot / cookies / extensions** → **无对应，不做**。

另外，WPF 有一批 chrome-devtools 没有但调试很有价值的能力，可以作为本机制的差异化亮点：依赖属性读/写、绑定表达式与绑定错误、路由事件监听、Adorner/Popup 遍历、资源字典、`DataTemplate`/`ControlTemplate` 检视。

## 四、功能需求

按「被控端」「主控」「工具集」三块列。

### 被控端（NuGet 包）

1. 提供一个内嵌入口，让宿主 WPF 程序在启动后（如 `Application.Startup` 或显式调用）拉起一个 named pipe server（固定 pipe 名），对外接受调试命令。
2. 在**宿主的 UI 线程**上执行所有碰 WPF 对象的命令（可视树遍历、属性读写、元素操作、截图、脚本里的 UI 访问），保证 STA 安全。
3. 维护**元素 uid 映射**：给拍到的可视树元素分配稳定 uid，跨快照对同一对象保持同一 id；提供按 uid 取回元素的能力。
4. 能**拍可视树快照**（指定窗口、可选深度/裁剪），产出文本和结构化两种形态。
5. 能**执行 VB.NET 脚本**，把结果序列化回传；脚本上下文里能访问 `Application.Current`、当前窗口、按 uid 的元素。
6. 能**渲染截图**并回传图像数据。
7. 能**采集事件流**：未处理异常、绑定错误、`Trace` 输出、窗口打开/关闭等，缓存或推送给主控。
8. 支持 net472 和 net8.0-windows 两个目标。

### 主控（McpServerForDevEnv）

1. 新增 **WPF 调试 tab**，单被控端模式：一个「连接/断开」动作 + 连接状态显示。被控端用固定的 pipe 名，主控不需要让用户填地址——直接连那个固定名即可。
2. 维护**到被控端的 named pipe 连接**（主控作 client，连被控端起的固定名 pipe server）。
3. 做**协议网关**：把外部 MCP 工具调用翻译成 pipe 命令，把被控端回的结果包成 MCP 响应。
4. **注册 WPF 调试工具**到对外工具列表，并按「是否连上被控端」控制可用性。
5. 复用现有的连接生命周期监控模式（参考 `VisualStudioMonitor` 的判活+失效事件+宿主清理）来监控被控端连接断开/进程退出。
6. 复用现有的主线程调度抽象（`IDispatcher`）、权限（`IMcpPermissionHandler`）、日志（`IMcpLogger`）。

### 工具集（MCP 对外）

按优先级分两批，命名沿用 chrome-devtools-mcp 的风格（snake_case），方便 AI 迁移直觉：

**MVP 第一批（核心闭环）：**
- `list_windows`：列出当前被控进程的所有窗口（含标题、是否主窗口、句柄），对应 chrome 的 list pages 但对象是 window。
- `take_snapshot`：拍当前/指定窗口的可视树文本快照（带 uid），对标 chrome 的 take_snapshot。
- `click`：按 uid 点击元素。
- `fill`：按 uid 给 `TextBox` 填值、给 `ComboBox`/`RadioButton`/`CheckBox` 选值。
- `evaluate`：在被控进程上下文执行一段 VB.NET 脚本并返回结果。
- `take_screenshot`：截当前窗口或指定 uid 元素。

**第二批（WPF 特色 + 增强）：**
- `get_property` / `set_property`：读写依赖属性。
- `get_logical_tree`：拍逻辑树快照。
- `list_bindings` / `get_binding_errors`：绑定调试。
- `highlight`：高亮指定 uid 元素（Snoop 风格）。
- `list_events`：读被控端采集的事件流（异常、绑定错误、Trace）。

每个工具的参数 schema、只读标记、是否需要「已连接被控端」等，在 plan 里给设计。

## 五、非功能需求

1. **多目标兼容**：被控端包必须同时跑在 net472 和 net8.0-windows 的 WPF 宿主上；两边的 API 差异（尤其程序集加载、脚本引擎）要有明确处理。
2. **STA / UI 线程安全**：所有 WPF 对象访问必须在宿主 UI 线程；IPC 线程不能直接摸元素。要避免「IPC 线程同步等 UI 线程、UI 线程又在等别的」造成的死锁。
3. **IPC 可靠性**：named pipe 的连接断开、被控进程崩溃、消息 framing、大对象（截图、大可视树）传输、并发请求排队都要处理。
4. **NuGet 包体积与启动成本**：被控端引入 Roslyn 脚本引擎会显著增大包体积、增加首次脚本编译延迟；要评估能否懒加载。
5. **安全边界**：被控端的 pipe 是本机的调试入口，能读写宿主进程任意对象、跑任意 VB.NET——这等于宿主进程的完全控制权。调试场景下可接受，但要在文档和权限提示里讲明，并尽量限制 pipe 只对本机/指定 SID 开放。
6. **与现有功能共存**：WPF 调试和现有 VS/DTE 调试要在同一个主控里和平共处，工具不冲突、连接互不干扰。

## 六、范围与边界（不做什么）

明确划出去，避免范围蔓延：

- **不做浏览器自动化**：不导航、不抓 network、不管 cookie/扩展。
- **不做远程注入**：被控端必须是自愿内嵌 NuGet 包的程序；不碰不愿意加依赖的程序。
- **不做跨机器**：pipe 限于本机；不搞远程代理。
- **不支持 VSIX**：WPF 调试只做独立 GUI 版（`McpServiceNetFx`），现有 `McpServiceNetFx.VsixAsync` 平行宿主不做同步。
- **脚本不做沙箱隔离**：VB.NET 脚本默认拥有宿主进程全部权限，不做能力隔离（调试用途）。
- **heap snapshot / performance trace**：不做（无对应场景）。

## 七、与 McpServerForDevEnv 现状的关系

基于 agent 摸出的现状基线，复用和新增点如下：

**可直接复用：**
- Tab 机制：往 `MainWindow.xaml` 的 `TabControlMain` 加一个 TabItem，新建 `MainWindow.WpfDebug.vb` partial，沿用现有 partial class 风格（无 MVVM）。
- 工具基类与注册：`VisualStudioToolBase`（`G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\VisualStudioToolBase.vb`）的模板方法模式、`VisualStudioToolManager` 的 `ConcurrentDictionary` + 手动 `RegisterTool`/`UnregisterTool`。
- 数据上下文注入模式：现有「选中实例 → `CreateVsTools` → `SetVsTools` 注入到每个工具」这套，WPF 调试照搬，只是 DC 从 `VisualStudioTools(DTE2)` 换成「被控端代理」。
- 连接监控模式：`VisualStudioMonitor`（`G:\Projects\McpServerForDevEnv\McpServiceNetFx\Helpers\VisualStudioMonitor.vb`）的「缓存标识 + 定时/事件判活 + 失效事件 + 宿主清理」，照搬到被控端连接监控（判活依据换成 pipe 状态 + 进程存活）。
- 抽象接口：`IDispatcher`（主线程调度）、`IMcpLogger`、`IMcpPermissionHandler`、`IClipboard`、`IInteraction`（`G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Helpers\Abstractions.vb`）。
- 协议处理器解耦：`VisualStudioMcpHttpService.ProcessRequest` 已经是纯 `JsonRpcRequest → JsonRpcResponse` 处理器，和 HTTP 传输事实分离，新增 pipe 传输可以复用同一套协议处理思路。

**需要新增：**
- 被控端 NuGet 包（独立解决方案/项目，双目标）。
- 主控和被控端共享的 Core（契约库，见下）。
- 被控端连接 UI（connect/disconnect + 状态，固定 pipe 名、单被控端，区别于现有枚举式 DataGrid）。
- named pipe 通道（传输层，主控侧 client + 被控端侧 server）。
- WPF 调试工具集（一批继承工具基类的工具）。
- 工具动态可见性机制（连上才出现，或 listChanged 推送）。

**关于 Core 抽取的关键判断：** 现有 `McpServiceNetFx.Core` 是 net472 单目标、依赖 EnvDTE 和 Newtonsoft，主控专用。被控端包双目标且不能要 EnvDTE，所以公共契约**不能直接塞进现有 Core**。合理做法是新建一个独立的共享契约库（多目标 net472;net8.0-windows，纯契约 + 公共机制，零 EnvDTE 依赖），被控端包和主控都引用它。具体命名和与现有 Core 的边界划分见 `rough-plan.md`。

## 八、已定决策

下面几条已经拍板，是设计的既定前提，方案里都按这个走：

1. **单被控端**：主控一次只服务一个被控进程（和现在对 VS 那样）。不做多被控端路由、不引入 targetId 路由。WPF 调试 tab 是「连一个、选中即当前」的模型。
2. **不支持 VSIX**：WPF 调试只在独立 GUI 版（`McpServiceNetFx`）实现，`McpServiceNetFx.VsixAsync` 不做同步。
3. **固定 pipe 名 + 协议版本**：被控端起一个写死的固定 pipe 名，名字里带协议版本号（如 `mcpserverfordevenv.wpfdebug.v1`），避免和别的版本/别的用途串扰。主控直接连这个固定名，不用让用户填地址；连上即发现被控端，握手时被控端回报自己的进程信息（pid、主窗口标题等）。单被控端 + 固定名意味着同一时刻只有一个被控端能占用这个 pipe，第二个想起的会撞名，按单被控语义报错即可。
4. **脚本权限 = 被控进程权限**：`evaluate` 的 VB.NET 脚本完全放开，权限等同于宿主进程本身，不做沙箱。在权限提示里写明这点。
5. **工具常驻、未连接时报错**：WPF 调试工具常驻在对外工具列表里，不追求「连上才出现」。没连上被控端时调用，按现有 VS 工具「未初始化」的风格报「未连接被控端」。不实现 `listChanged` 推送。
