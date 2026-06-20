# McpServerForDevEnv：MCP 通信机制与 DTE 实例管理机制

本文讲 McpServerForDevEnv 这个项目里最关键的两件事：一是它作为 MCP server 怎么和客户端通信，二是它怎么拿到并管理 Visual Studio 的 DTE 实例。项目整体定位和构建方式只在开头带过，重点全放在这两块上。引用的源码都标了 Windows 绝对路径，方便直接跳过去核对。

## 项目是个什么东西

McpServerForDevEnv 是一个把本地 Visual Studio 的能力（构建解决方案、构建项目、读错误列表、读活动文档、跑自定义工具等）通过 MCP 协议暴露给 AI 编程助手的桥梁。它有两种部署形态，共用同一套核心：

- **独立服务管理器**：一个 WPF 桌面程序（`McpServiceNetFx` 项目），启动后从本机所有运行中的 VS 里挑一个，在本地起一个 HTTP 端口对外提供 MCP 服务。
- **VSIX 内嵌服务管理器**：装进 Visual Studio 里的插件（`McpServiceNetFx.VsixAsync` 项目），只能控制「当前这个」VS，不跨进程去找别的 VS。

两种形态真正的业务逻辑都落在类库 `McpServiceNetFx.Core` 里——MCP 协议解析、工具注册和执行、DTE 操作的封装全在这里，前面两个壳只是负责拿到 DTE2、配端口、起服务、管权限和日志。整个解决方案是 VB.NET + .NET Framework 4.7.2（测试项目另外编了 net8/net10 的目标），依赖里和本文相关的就两个：`Microsoft.VisualStudio.Interop`（提供 `EnvDTE`/`EnvDTE80` 的互操作类型）和 `Newtonsoft.Json`（序列化）。注意它**没有**引用任何官方/社区的 MCP SDK，整套 JSON-RPC 是手写的——这一点后面会反复提到。

## MCP 通信机制

### 传输方式：本地 HTTP + 手写 JSON-RPC

它走的是 **HTTP 传输**，不是 stdio，也不是 SSE。底层完全没用 MCP SDK，用的是 .NET Framework 自带的 `System.Net.HttpListener`，自己实现了一份 JSON-RPC 2.0 的收发。入口在 `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Mcp\McpService.vb`，这是服务的总管，负责起停监听、收请求、写响应。

监听地址是 `http://+:{port}/`，也就是本机所有网卡。`StartAsync` 里调 `StartHttpListenerAsync`，里面先建 `HttpListener`、加前缀、`Start()`，然后用一个 `CancellationTokenSource` 配合 `Task.Run` 跑一个 `ListenLoopAsync` 监听循环。`+` 这个通配前缀在 Windows 上需要 urlacl 授权，所以如果抛了 `HttpListenerException`，代码会把对应的 `netsh http add urlacl ...` 命令拼好、试着复制到剪贴板，再弹一个 `UserMessageWindow` 提示用户用管理员权限跑一遍——这套处理在 `McpService.vb` 的 `StartHttpListenerAsync` 和 `TryCopyToClipboard`/`ShowUserMessageWindowAsync` 里。

对外暴露的路由就两条，在 `HandleRequestAsync` 里按 path + method 分流：

- `GET /mcp/status`：健康检查，返回 `VisualStudioMcpHttpService.GetStatus()` 给的状态字典（status/service/version/timestamp）。
- `POST /mcp`：所有真正的 MCP 请求都走这里。其余路径一律 404。

README 里强调「Service binds to local network loopback only」，但代码监听的是 `+`。实际约束 urlacl 授权范围是用户自己 `netsh` 时的事，这点值得留意——代码层面并没有强制绑回 `127.0.0.1`。

### JSON-RPC 的收发与分发

监听循环的设计是 **一个请求一个任务、彼此不阻塞监听**。`ListenLoopAsync` 里每 `GetContextAsync` 拿到一个 context，立刻 `Task.Run(Function() SafeHandleRequestAsync(context, token))` 派出去处理，循环马上回头等下一个连接。`SafeHandleRequestAsync` 是个保护壳，里头 `HandleRequestAsync` 抛什么异常都接住，最多回个 500 然后关掉响应，不让单个坏请求把整个服务拖死。

具体到一条 `POST /mcp`：

1. 从 `ctx.Request.InputStream` 把请求体读成字符串（编码优先取 `ContentEncoding`，没有就用 UTF-8）。
2. 用 `JsonConvert.DeserializeObject(Of JsonRpcRequest)` 反序列化成 `JsonRpcRequest`。
3. 交给 `VisualStudioMcpHttpService.ProcessMcpRequest(request)` 处理，拿到 `JsonRpcResponse`。
4. 如果响应是 `Nothing`（说明这是一条通知），回 `204 No Content`；否则 `WriteJsonAsync` 把响应序列化成缩进好的 JSON 写回去。

真正分发请求的逻辑在 `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Mcp\VisualStudioMcpHttpService.vb` 的 `ProcessRequest`，一个 `Select Case request.Method`：

- `initialize`：`ProcessInitialize`，返回 `protocolVersion`（`"2024-10-07"`）、`capabilities`（只声明了 `tools`，且 `listChanged` 为 `False`）、`serverInfo`（name=`Visual Studio MCP Server`、version=`1.0.0`）和一段 `instructions`。
- `notifications/initialized`：纯通知，按规范不该带 id；如果带了 id 会被当成 Invalid Request 拒掉，正常情况下返回 `Nothing`（不回响应）。
- `tools/list`：`ProcessToolsList`，从工具管理器拿工具定义数组。
- `tools/call`：`ProcessToolsCall`，真正执行工具。
- `ping`：`ProcessPing`，按规范回个空对象。
- `notifications/canceled`、`prompts/list`、`resources/list`：注释标着 `Reserved`，占位但不实现。
- 其它一律 `-32601 Method not found`。

错误码是标准的 JSON-RPC 那套：`-32700` Parse error、`-32600` Invalid Request、`-32601` Method not found、`-32602` Invalid params、`-32603` Internal error。这套常量在 `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Mcp\McpExceptions.vb` 的 `McpErrorCode` 模块里集中定义。同一个文件里的 `McpException` 类注释很有意思——它明说「用于替换 ModelContextProtocol 库中的异常类型」，说明项目早期是打算用 MCP 库的，后来改成全手写，异常类型也跟着换了一套自己的。

### 工具怎么注册、声明 schema、被调用

工具相关的数据模型集中在 `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Models\McpModels.vb`。`ToolDefinition` 有三个字段：`name`、`description`、`inputSchema`（一个 `InputSchema`，含 `type`/`properties`/`required`）。`inputSchema.properties` 是个 `Dictionary(Of String, PropertyDefinition)`，每个 `PropertyDefinition` 有 `type`、`description`、可选的 `default`。也就是说 schema 是**手填的 JSON Schema 片段**，不是像 chrome-devtools-mcp 那样用 zod 推导——这是「没有 MCP SDK」的必然结果。

注册和执行的中枢是 `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\VisualStudioToolManager.vb`。几个要点：

- 工具存在 `ConcurrentDictionary(Of String, VisualStudioToolBase)` 里，key 是工具名。
- 注册是**手动 `New` 出来逐个 `RegisterTool`**，不是反射扫描。`RegisterAllToolsWithoutContext` 里的注释直说「手动注册所有工具，避免反射的性能开销」。预注册了七组 VS 工具（build_solution、build_project、run_custom_tools、get_error_list、get_solution_info、get_active_document、get_all_open_documents）和七组文件操作/搜索工具（其中 `ReadLinesTool`/`ReplaceLinesTool` 各注册了两份，靠构造参数区分出两个不同工具名）。顺带一提，仓库里还有个 `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Mcp\VisualStudioMcpTools.vb`，看签名是用 `<Description(...)>` 特性 + 反射来声明工具的旧实现，现在已经被手动注册方案取代、属于历史遗留。
- 工具分**两阶段初始化**：构造管理器时只注册工具框架、不带 DTE 上下文（`_vsTools = Nothing`）；等用户选好 VS 实例后，调 `CreateVsTools(dte2, dispatcher)` 真正建出 `VisualStudioTools`，再 `SetVsTools` 注入到每个工具里。`CreateVsTools` 有个去重判断：如果传入的 DTE 引用和上次是同一个（`_currentDte Is dte2`），就跳过重建。`_isInitialized` 没置位之前，`ExecuteToolAsync` 会直接抛「工具管理器未初始化」。
- `GetToolDefinitions` 把所有工具的 `ToolDefinition` 投影成数组，喂给 `tools/list`。
- `ExecuteToolAsync(toolName, arguments)` 是执行入口：查存在性、取工具、记日志、`Await tool.ExecuteAsync(arguments)`。

工具基类在 `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\VisualStudioToolBase.vb`。`ExecuteAsync` 是个模板方法：先确认 `_vsTools` 已设置（没设置就抛 McpException），再调子类必须实现的 `ExecuteInternalAsync`，过程中所有非 McpException 的异常都被包成 `McpException`（`InternalError`）。基类还提供权限检查（`CheckPermission`/`CheckFilePermission`，委托给 `IMcpPermissionHandler`）、必需参数校验（`ValidateRequiredArguments`）、可选参数取值（`GetOptionalArgument(Of T)`，内部用 `Convert.ChangeType` 转型）。每个工具还要声明 `DefaultPermission`（Allow/Ask/Deny）和 `IsFileTool`（文件工具才走路径策略检查）。

一个典型工具可以看 `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\BuildSolutionTool.vb`：构造时用 `New ToolDefinition With { ... }` 直接把 name/description/inputSchema 写死成只读属性，`ExecuteInternalAsync` 里先 `CheckPermission`，再从 arguments 取可选的 `configuration`（默认 `Debug`），调 `_vsTools.BuildSolutionAsync`，把结果包成 `BuildResultResponse` 返回。

### 工具调用的请求与响应怎么拼

`tools/call` 的处理在 `VisualStudioMcpHttpService.HandleToolCall`。参数反序列化成 `ToolCallParams`（`name` + `arguments` 字典）。流程是：找不到工具就返回 `CallToolErrorResult`（带 `isError=true`、`errorMessage`）；找到了就 `ExecuteToolAsync`，成功结果包成 `CallToolSuccessResult`。

响应拼装有个值得注意的细节：成功结果**同时塞了两份内容**。一份是 `StructuredContent`（直接放工具返回的对象，符合 MCP 的 `structuredContent` 字段）；另一份把这个对象再 `JsonConvert.SerializeObject(..., Formatting.Indented)` 序列化一遍，塞进 `Content` 列表里当一个 `TextContentBlock`。也就是说同一个结果，结构化和文本两路都给客户端，方便既能程序消费又能直接读。`CallToolSuccessResult`/`CallToolErrorResult` 都继承自 `CallToolResultBase`（后者多个 `isError`、`errorMessage`、`errorCode`、`errorDetails`）。内容块还预备了 `ImageContentBlock`/`AudioContentBlock`/`EmbeddedResourceBlock`/`ResourceLinkBlock` 等类型，但目前工具实际只产文本块。

### 会话、连接、并发

这一块和典型 MCP server 不太一样，值得单独说清楚。

**没有 session 概念**。每条 `POST /mcp` 都是独立请求，服务端不维护客户端会话、不存连接状态，initialize 之后也没有 token 之类的凭证；靠的是「本地 loopback + 一次只服务一个 VS 实例」的隐含约定。

**并发模型是「监听串行、处理并行、工具执行靠线程模型兜底」**：

- 监听循环本身是单任务串行的（一个 `While` 循环），但拿到 context 后立即 `Task.Run` 派发，所以**多个请求可以同时在被处理**，彼此不阻塞监听。这里没有像 chrome-devtools-mcp 那样的全局工具互斥锁。
- 真正的串行化发生在 DTE 调用那一层。所有 DTE 操作都通过 `IDispatcher.InvokeAsync` 切到 UI/主线程上执行（见 DTE 那节），所以哪怕 HTTP 层同时进来十个 `tools/call`，最终落到 DTE 上的调用也会被 UI 线程的消息队列排成串行。换句话说，并发保护不在 MCP 层，而在「DTE 只能在主线程摸」这个 COM/STA 约束上。
- `VisualStudioToolManager` 用 `ConcurrentDictionary` 存工具，注册/注销是线程安全的；`ExecuteToolAsync` 本身没有加锁。

**没有显式的请求队列或取消机制**。`notifications/canceled` 在 `ProcessRequest` 里是 Reserved 不处理，所以一个已经开始执行的工具调用（尤其是一次可能跑很久的解决方案构建）没法被中途取消——构建工具内部是靠轮询 `SolutionBuild2.BuildState` + `Task.Delay(100)` 等它自己跑完的。

## DTE 实例管理机制

这一节按「发现 / 获取 / 多实例选取 / 生命周期 / 线程模型」五个方面讲，两条部署路径（独立 WPF 和 VSIX）的区别会穿插着说。

### 怎么发现运行中的 VS 实例

独立 WPF 程序用的是 **Running Object Table（ROT）**，代码在 `G:\Projects\McpServerForDevEnv\McpServiceNetFx\Helpers\VisualStudioEnumerator.vb`。它通过 P/Invoke 直接调 `ole32.dll` 的 `GetRunningObjectTable` 和 `CreateBindCtx`，拿到 `IRunningObjectTable` 后 `EnumRunning` 遍历所有 moniker。判断一个 moniker 是不是 VS，靠的是它的 display name：代码注释里写明了预期格式是 `!VisualStudio.DTE.18.0:17752` 这种——前缀 `!VisualStudio.DTE.` 表示是 VS 的 DTE 对象，冒号后面的数字是进程 ID。所以版本区分不是写死某个版本号，而是**按 display name 前缀匹配 + 用 `:` 后的 pid 定位单个实例**，对 VS 2015（14.0）到最新版（18.0）都能覆盖，这跟 README 说的「Support Visual Studio from 2015 to latest version」对得上。

每命中一个候选，`ProcessMoniker` 会：`rot.GetObject` 拿到注册对象，`TryCast` 成 `EnvDTE80.DTE2`；再用 `IsMainWindowValid` 做一道有效性过滤——不仅要 `dte2.MainWindow` 非空，还要 `IsWindow(hWnd)` 为真、且窗口标题包含 `Visual Studio`。这一步是为了挡掉那些已经进 ROT 但主窗口已经没了、或者标题对不上的失效实例。最后填出一个 `VisualStudioInstance`（Caption/SolutionPath/Version/ProcessId/DTE2）。

VSIX 路径不需要发现，因为它就跑在 VS 自己进程里，直接拿「当前这一个」。

### 怎么拿到 DTE 对象：附加 vs. 启动新实例

**两条路径都是「附加到已有 VS」，没有任何启动新 VS 实例的代码。** 全仓库搜 `CreateObject`/`New ProcessStartInfo`/`devenv.exe` 这类启动新进程的用法，命中的要么是打开帮助链接、要么是 Research 下的实验项目，没有任何地方会拉起一个新的 Visual Studio。这点和 README 的定位一致——它是「把已有的 VS 暴露出去」，不是「替你开 VS」。

获取 DTE 的两种来源：

- **独立 WPF（跨进程 COM）**：通过 ROT 拿到的 `DTE2` 是一个跨进程的 COM 代理（RCW）。MCP server 进程和 VS 进程不是同一个，所有 DTE 调用都是跨进程 COM 调用。
- **VSIX（进程内）**：在 `G:\Projects\McpServerForDevEnv\McpServiceNetFx.VsixAsync\ToolWindows\McpWindowState.vb` 里，`_dte2 = package.GetService(Of SDTE, DTE2)()`，这是走 VS Shell 自己的服务容器拿到的 DTE，和插件跑在同一个进程里，没有跨进程 COM 的开销。这也解释了为什么 VSIX 文档里写「Does not support controlling other Visual Studio instances, only the current Visual Studio instance」——它根本不经过 ROT。

两条路径拿到 DTE2 之后，后面是同一套：`VisualStudioToolManager.CreateVsTools(dte2, dispatcher)` 构造 `VisualStudioTools(dte2, dispatcher, logger)`，再注入到所有工具。

### 多实例怎么管理和选取

**核心策略是「一次只服务一个 VS 实例」，由用户在 UI 上选。** 不存在一个 server 同时管多个 DTE 的多路复用。

- 独立 WPF：`G:\Projects\McpServerForDevEnv\McpServiceNetFx\Views\MainWindow.VsInstances.vb` 里，`RefreshVsInstances` 调 `VisualStudioEnumerator.GetRunningInstances()` 把所有运行实例填进一个 DataGrid；用户选中一行触发 `DgVsInstances_SelectionChanged`，存到 `_selectedVsInstance`，然后 `CreateToolManagerDataContext` 把它的 DTE2 交给工具管理器。刷新列表时还会校验当前选中的实例还在不在（按 `ProcessId` 比），不在就清空选中。`_selectedVsInstance` 就是选取目标实例的 key——本质是「用户手动指定的那一个」。
- 选实例的 key 是 **进程 ID**（`VisualStudioInstance.ProcessId`，来自 ROT moniker 的 pid），辅以 Caption/SolutionPath 给人看。工具管理器内部用「DTE2 引用相等」（`_currentDte Is dte2`）做重复初始化的去重。
- 服务启动后，选择就锁死了：`UpdateServiceUI(True)` 里会把 DataGrid、刷新按钮、搜索框、端口框全部 `IsEnabled = False`，想换实例必须先停服务。停服务时（`UpdateServiceUI(False)`）会主动 `_selectedVsInstance = Nothing` 并重新刷一遍列表。
- VSIX：只有一个实例，`McpWindowState` 构造时就 `GetService` 拿到 DTE2 并立刻 `CreateVsTools`，没有选择这一步。

值得注意的一点：`G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Mcp\McpService.vb` 的构造函数收了 `_dte2` 并存成字段，但这个字段在 `McpService` 内部**从头到尾没被用过**——真正干活的 DTE2 是经 `VisualStudioToolManager` → `VisualStudioTools` 这条链路走的。`McpService._dte2` 更像是历史遗留或预留的引用，看代码时别被它误导。

### 实例失效 / VS 关闭怎么处理

这部分主要由 `G:\Projects\McpServerForDevEnv\McpServiceNetFx\Helpers\VisualStudioMonitor.vb` 负责，独立 WPF 路径在启动服务时为选中的 DTE 建一个 `VisualStudioMonitor`（见 `MainWindow.McpService.vb` 的 `StartMcpService`）。

监控靠**两套机制叠加**，互相兜底：

1. **DTE 事件**：构造时 `AddHandler _dte2.Events.DTEEvents.OnBeginShutdown, AddressOf OnVisualStudioShutdown`。VS 正常关闭时会触发 `OnBeginShutdown`，走到 `VisualStudioShutdown` 事件。如果挂 handler 这一步就抛异常，说明 DTE 已经断了，直接 `HandleDTEDisconnected`。
2. **窗口句柄轮询**：构造时缓存 `_cachedHWnd = dteInstance.MainWindow.HWnd` 和原始 Caption，然后起一个 `System.Timers.Timer`，**每 2 秒**检查一次。检查逻辑是 `IsWindow(hWnd)` 看句柄还合不合法，再看 `GetWindowText` 取到的标题里还有没有 `Visual Studio`——第二道检查是为了防止「句柄被别的窗口重用」造成误判。一旦判定实例已退出，停掉 timer 并抛 `VisualStudioExited` 事件。

`IsAlive()` 方法合并了这两个信号：DTE 非空、`MainWindow` 非空、缓存句柄仍 `IsWindow`。`Dispose` 时摘事件、停 timer、移除 handler。

`MainWindow.McpService.vb` 把 `VisualStudioExited` 和 `VisualStudioShutdown` 两个事件都接住，回调里 `SafeBeginInvoke` 切回 UI 线程，记日志、调 `CleanupService`（停掉并 Dispose `McpService`、Dispose `VisualStudioMonitor`、更新 UI 状态），然后弹窗告诉用户「实例退出了，服务已停」。VSIX 那边没有对应的 monitor——因为插件本身就活在 VS 进程里，VS 关了插件也跟着没，包的 `Dispose` 里调一下 `StopService` 就行（见 `McpServiceNetFx.VsixAsyncPackage.vb`）。

需要提醒一句：这套监控是**通知性的**，不是防御性的。从「VS 真正关闭」到「轮询发现、事件回调、清理」之间有个最多 2 秒的窗口，这段时间里如果客户端正好发来 `tools/call`，工具仍会试着在已经失效的 DTE 上操作，靠的是 DTE 调用自己抛 COM 异常被各工具的 try/catch 兜底（`VisualStudioTools` 里几乎每个方法都包了 `Try/Catch`，异常被吞进结果对象）。没有「调用前先检查 IsAlive 再拒绝」这样的前置闸门。

### DTE 调用的线程模型

这是整个机制最需要小心的地方，也是前面说的「并发保护其实在这一层」的落点。

**EnvDTE 是 STA / COM 自动化对象，必须在创建它的线程（VS 的 UI 主线程）上访问。** 项目用一个 `IDispatcher` 接口（`G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Helpers\Abstractions.vb`）把这个约束抽象出来，所有 DTE 操作一律 `_dispatcher.InvokeAsync(...)` 切到主线程再执行。`VisualStudioTools`（`G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\VisualStudioTools.vb`）里每个方法——`BuildSolutionAsync`、`BuildProjectAsync`、`GetErrorListAsync`、`GetSolutionInformationAsync`、`GetActiveDocumentAsync`、`GetAllOpenDocumentsAsync`、`RunProjectCustomToolsAsync`——都是这个模式：把对 `_dte2.Solution`/`_dte2.ToolWindows`/`_dte2.Documents` 的访问整个包在 `Await _dispatcher.InvokeAsync(Async Function() ...)` 里。

`IDispatcher` 有两个实现，分别对应两条部署路径，但干的是同一件事——把任务排到主线程：

- **独立 WPF**：`MainWindow.McpService.vb` 里的内嵌 `DispatcherService`，包装 WPF 的 `Dispatcher.BeginInvoke`。实现上用 `TaskCompletionSource` 等一个 `Async Sub` 跑完，把异常通过 `SetException` 透出来。这个 dispatcher 跑的是 **MCP server 进程自己的 UI 线程**——注意，跨进程 COM 调用时，被调用的 STA 对象（VS 里的 DTE）会由 COM 编组（marshal）回 VS 进程的主线程执行，所以「切到 server 进程的 UI 线程」和「最终在 VS 主线程上执行」是通过 COM 的 STA 编组串起来的。
- **VSIX**：`McpWindowState.vb` 里的 `DispatcherService`，包装 `JoinableTaskFactory.SwitchToMainThreadAsync`。这是 VS SDK 推荐的主线程切换方式，和 VS 自己的线程规则对齐。

这里有个容易混淆的点要分清：**独立 WPF 模式下，DTE 是跨进程 COM 代理**，所以 `VisualStudioTools` 里的每次 `_dte2.xxx` 访问都是一次跨进程 COM 往返，外加 COM 把调用编组到 VS 主线程的开销；**VSIX 模式下是同进程同线程**，开销小得多。两种模式共享同一份 `VisualStudioTools` 代码，靠不同的 dispatcher 实现屏蔽差异。这也是为什么「构建解决方案」这种长操作里要 `Task.Delay(100)` 轮询 `BuildState`——`SolutionBuild2.Build(False)` 是非阻塞触发的，得自己在主线程上反复回查状态。

另外，ROT 拿到的 DTE2 这个 RCW，项目全程没有显式 `Marshal.ReleaseComObject`，靠 GC 和进程退出自然回收；`VisualStudioEnumerator.ProcessMoniker` 里只对枚举过程中的 `moniker` 和 `bindCtx` 做了 `Marshal.ReleaseComObject`。这在「实例就一个、生命周期和服务绑定」的场景下基本够用，但如果想做成支持动态切换多实例的长驻服务，这套 COM 引用管理就需要重新设计了。

## 小结：两条主线怎么拼起来

把两块串起来看，一条 `tools/call` 从进来到落到 VS 上的完整链路是：

客户端 `POST /mcp`（JSON-RPC `tools/call`）→ `HttpListener` 监听循环 → `Task.Run` 派发 → `McpService.HandleMcpRequestAsync` 读体反序列化 → `VisualStudioMcpHttpService.ProcessMcpRequest` → `ProcessToolsCall` → `VisualStudioToolManager.ExecuteToolAsync` → `VisualStudioToolBase.ExecuteAsync`（查权限、参数）→ 子类 `ExecuteInternalAsync` → `VisualStudioTools.XxxAsync` → `_dispatcher.InvokeAsync` 切主线程 → `_dte2` 的 COM 调用（独立模式下跨进程编组到 VS 主线程）→ 结果回程，结构化 + 文本两份塞进 `CallToolSuccessResult` → JSON-RPC 响应写回 HTTP。

整条链路里，**「没有 MCP SDK、HTTP + 手写 JSON-RPC」** 和 **「DTE 全靠 IDispatcher 切主线程 + ROT/GetService 两种来源」** 是两个最核心的设计决定，剩下的工具注册、权限、监控都是围绕这两个骨架搭的。

## 相关源码（绝对路径）

**MCP 通信**

- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Mcp\McpService.vb` — HTTP 服务的总管：起停 `HttpListener`、监听循环、请求分流、响应写入、端口授权失败的处理。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Mcp\VisualStudioMcpHttpService.vb` — JSON-RPC 分发核心：`ProcessRequest` 的方法路由、initialize/tools/list/tools/call/ping 的处理、成功/错误响应构造。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Models\McpModels.vb` — `JsonRpcRequest`/`JsonRpcResponse`/`JsonRpcError`/`ToolDefinition`/`InputSchema`/`CallToolSuccessResult`/`CallToolErrorResult`/各种 `ContentBlock` 等数据模型。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Mcp\McpExceptions.vb` — `McpException`、`McpErrorCode` 常量（注释里提到这是替换 ModelContextProtocol 库异常的自定义类型）。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\VisualStudioToolManager.vb` — 工具注册/发现/执行中枢：`ConcurrentDictionary` 存储、手动注册、两阶段初始化、`ExecuteToolAsync`。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\VisualStudioToolBase.vb` — 工具基类：模板方法 `ExecuteAsync`/`ExecuteInternalAsync`、权限检查、参数校验。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\BuildSolutionTool.vb` — 一个典型工具的写法（name/description/inputSchema 直接写死、调 `_vsTools`）。

**DTE 实例管理**

- `G:\Projects\McpServerForDevEnv\McpServiceNetFx\Helpers\VisualStudioEnumerator.vb` — ROT 枚举：`GetRunningObjectTable`/`CreateBindCtx` 的 P/Invoke、moniker display name 解析（`!VisualStudio.DTE.<ver>:<pid>`）、DTE2 获取与主窗口有效性过滤、`VisualStudioInstance` 数据类。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx\Helpers\VisualStudioMonitor.vb` — 实例生命周期监控：`DTEEvents.OnBeginShutdown` 事件 + 2 秒间隔的窗口句柄/标题轮询、`IsAlive`、`Dispose`。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx\Views\MainWindow.VsInstances.vb` — 独立 WPF 的实例列表刷新、选中、`CreateToolManagerDataContext`。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx\Views\MainWindow.McpService.vb` — 启停服务：构造 `VisualStudioMonitor` 并接事件、起 `McpService`、退出/关闭时的 `CleanupService`、内嵌的 `DispatcherService`（WPF Dispatcher 包装）。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.VsixAsync\ToolWindows\McpWindowState.vb` — VSIX 路径：`package.GetService(Of SDTE, DTE2)()` 进程内取 DTE、`DispatcherService`（`JoinableTaskFactory.SwitchToMainThreadAsync` 包装）、权限/日志实现。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Tools\VisualStudioTools.vb` — DTE 操作的统一封装：所有方法都用 `_dispatcher.InvokeAsync` 切主线程后访问 `_dte2`，是线程模型约束的落点。
- `G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Helpers\Abstractions.vb` — `IDispatcher`/`IClipboard`/`IInteraction` 抽象接口。
