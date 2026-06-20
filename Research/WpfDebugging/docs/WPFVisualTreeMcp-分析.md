# WpfVisualTreeMcp 项目分析

本文把 `G:\Projects\WPFVisualTreeMcp` 这个项目从头到尾捋一遍，目标是让做 WPF 调试研究的人能快速看懂：它到底干什么、怎么搭起来的、核心模块怎么配合、关键流程长什么样、又怎么构建和跑起来。引用到的源码文件一律用 Windows 绝对路径标出，方便直接跳过去核对。

## 一句话定位

WpfVisualTreeMcp 是一个 MCP（Model Context Protocol）服务器，作用是让 AI 编程代理（Claude Code、Cursor、Copilot 之类）能够「看进」正在运行的 WPF 应用，干的事跟 Snoop WPF 或 Visual Studio 的 Live Visual Tree 基本一样：枚举进程、读可视树和逻辑树、读依赖属性和绑定、抓绑定错误、看资源字典和样式、截图、高亮元素，甚至能反过来驱动控件（点击、填字、发快捷键）。它把这些能力通过 MCP 协议以 20 个工具的形式暴露出去，模型按需调用。

它和被调试的 WPF 应用之间是**多进程**关系，这点很关键：MCP 服务器自己是个独立的 .NET 8 进程，要探查的目标 WPF 应用是另一个进程；服务器通过「向目标进程注入一个 Inspector DLL + 命名管道通信」的方式隔着进程边界读可视树。这么做是为了安全——服务器崩了不会拖垮被调试的应用，反过来也一样。

## 整体结构和技术栈

解决方案 `G:\Projects\WPFVisualTreeMcp\WpfVisualTreeMcp.sln` 里有七个项目，分三组（src、samples、tests）。各项目的定位和技术栈如下：

| 项目 | 输出 | 目标框架 | 角色 |
|---|---|---|---|
| `WpfVisualTreeMcp.Server` | exe | net8.0 | MCP 服务器（stdio）+ 同一个 exe 兼做一次性 CLI |
| `WpfVisualTreeMcp.Shared` | dll | net8.0;net48 | 跨进程共享的 IPC 消息契约和结果模型 |
| `WpfVisualTreeMcp.Inspector` | dll | net48;net8.0-windows | 被注入到目标 WPF 进程里的探查核心（双目标） |
| `WpfVisualTreeMcp.Injector` | dll | net48;net8.0 | 托管层注入逻辑（CreateRemoteThread + LoadLibrary） |
| `WpfVisualTreeMcp.InjectorHelper` | exe | net8.0，PlatformTarget=x86 | 32 位助手 exe，解决 64 位服务器注入 32 位目标的位宽不匹配 |
| `WpfVisualTreeMcp.Bootstrapper` | dll（原生 C++） | Win32 / x64 | 注入进去之后负责把 .NET 运行时拉起来、加载托管 Inspector |
| `SampleWpfApp`（samples） | exe | net8.0-windows | 自带一个 WPF 测试应用，启动时直接初始化 Inspector |

另外 `tests\WpfVisualTreeMcp.Tests` 是 xUnit + Moq + FluentAssertions 的单元测试。

几个值得注意的技术选型：

- 服务器用的是**官方的 C# MCP SDK**（`ModelContextProtocol` 0.4.1-preview.1），不是手搓 JSON-RPC。工具靠 `[McpServerTool]` 特性标注、由 `WithToolsFromAssembly()` 自动发现注册，协议走 stdio。
- 服务器侧日志用 Serilog，但有一条铁律：**stdout 必须干净**，因为 MCP 协议的 JSON-RPC 帧走 stdout。所有日志都重定向到 stderr 和文件（`%LOCALAPPDATA%\WpfVisualTreeMcp\logs\`），见 `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\Program.cs`。
- Inspector 故意做成**双目标**（net48 + net8.0-windows）。net48 这份给传统 .NET Framework WPF 应用用，net8.0-windows 这份给 .NET 5+/8 的 WPF 应用用。两份对应两套不同的 CLR 注入路径（下面细说）。
- 依赖方面，服务器只引了 `Microsoft.Extensions.Hosting`/`Logging`、`ModelContextProtocol`、`Serilog.*` 和 `System.IO.Pipes`，整体很轻。

## 对外提供哪些能力：20 个 MCP 工具

所有工具集中在 `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\WpfTools.cs` 这一个文件里，类 `WpfTools` 标了 `[McpServerToolType]`，每个方法标了 `[McpServerTool]`，方法名按 PascalCase 写、SDK 自动转成下划线小写的工具名（如 `WpfListProcesses` → `wpf_list_processes`）。工具分两大类：**只读探查**（17 个）和**会改应用状态**（3 个）。

只读那批覆盖了 Snoop 风格的全部常规操作：列进程、attach、读可视树、读元素属性、按类型/名字找元素（含跨所有窗口的深搜）、读绑定和绑定错误、读 DataContext 及其继承链、枚举资源、读样式/模板、监视属性变化、高亮元素、读布局信息、导出可视树（JSON 或 XAML）、截图。会改状态的是 `wpf_click_element`、`wpf_set_text`、`wpf_send_keys` 三个，它们走 UI Automation 或真实的 OS 鼠标/键盘输入去驱动控件，工具描述里都显式标了 `STATE-CHANGING` 提醒模型。

工具方法的实现非常薄：校验参数，然后调注入的 `IIpcBridge` 把请求转发给目标进程里的 Inspector。比如 `WpfGetVisualTree` 就是先钳制 `max_depth` 到 1–100，再 `_ipcBridge.GetVisualTreeAsync(root_handle, max_depth)`。真正干活的全在 Inspector 那侧。截图工具稍微特殊一点，它直接返回 MCP 的 `CallToolResult`，里面塞一个 `ImageContentBlock`（base64 PNG）加一行文字说明——这样模型能直接「看到」截图，不用再走文件。

同一个可执行文件还能当**一次性 CLI** 用。`Program.cs` 在最前面判断 `args[0]` 是不是已知的子命令（`CliRunner.IsCliCommand`），是就走 CLI 模式，否则起 MCP stdio 服务器。CLI 提供完全相同的 20 个能力（子命令名是 `list`/`attach`/`tree`/`find`/`click`/`screenshot` 这种短词），输出 JSON 到 stdout、诊断到 stderr，每次调用都是无状态的。这套前端的实现是 `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\Cli\CliRunner.cs`，它直接复用 `ProcessManager` 和 `NamedPipeBridge`，只是不走 MCP 握手。这么做的好处是：MCP 没连上时、脚本里、或者人手动验证流水线时，都能直接调。

## 核心模块和它们怎么配合

整个系统可以分成三层，每层住在不同的进程里，靠命名管道串起来。

### 第一层：MCP 服务器（独立进程）

入口在 `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\Program.cs`。它用 generic host 起服务，注册两个单例：`IProcessManager`（实现是 `ProcessManager`）管「找哪个进程、attach 上去、要不要注入」，`IIpcBridge`（实现是 `NamedPipeBridge`）管「跟目标进程里的 Inspector 收发请求」。`WpfTools` 通过构造函数注入拿到这两个服务。

`ProcessManager` 在 `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\Services\ProcessManager.cs` 里。列进程靠枚举系统进程，对每个进程看它加载的模块里有没有 `PresentationFramework`/`PresentationCore`/`wpfgfx`，有就当作 WPF 应用；同时顺便读 `clr.dll`/`coreclr.dll` 判断它是 Framework 还是 CoreCLR、拿到版本号。attach 时它会先看目标进程里是不是已经加载了 `WpfVisualTreeMcp.Inspector.dll`（自宿主模式，比如 SampleWpfApp 启动时自己初始化的）；如果没有、但调用方传了 `auto_inject=true`，就调 `ProcessInjector` 把 Inspector 注进去，然后轮询等命名管道 `wpf_inspector_{pid}` 出现（最多等 10 秒）。`ProcessManager` 只记一个「当前会话」`InspectionSession`，里面就是 PID、会话 ID、主窗口句柄和 Inspector 状态。

`NamedPipeBridge` 在 `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\Services\NamedPipeBridge.cs`。它实现 `IIpcBridge` 的每个方法套路都一样：`EnsureConnected()` 拿到当前会话，构造对应的请求对象，`SendRequestAsync` 发出去，拿到响应后解析成强类型结果。发送细节有几个值得说的点：连接超时 5 秒、请求超时 30 秒；每次请求都先 `Process.GetProcessById` 检查目标还活着没，死了就返回一句引导模型重新 attach 的友好错误；管道名永远是 `wpf_inspector_{pid}`；读写用 `StreamReader`/`StreamWriter`（注意这跟 Inspector 那侧不一样，下面解释为什么）。

### 第二层：注入管线（一次性，把 Inspector 塞进目标进程）

这是整个项目技术含量最高的部分，分成托管注入器、原生 bootstrapper、位宽助手 exe 三块配合。

托管注入器是 `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Injector\ProcessInjector.cs`，核心方法 `InjectIntoProcess(processId, inspectorDllPath)`。它走的是经典的 **CreateRemoteThread + LoadLibraryW** 套路，但注入的不是 Inspector 本身，而是一个**原生 bootstrapper DLL**——因为托管 DLL 没法被 LoadLibrary 直接「跑起来」，得有人先把 CLR 拉起来再加载它。具体步骤是：`OpenProcess` 拿到目标进程句柄（要 `PROCESS_CREATE_THREAD | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ | PROCESS_QUERY_INFORMATION`），`VirtualAllocEx` 在目标进程里分一块内存，`WriteProcessMemory` 把 bootstrapper 的完整路径写进去，`GetProcAddress(kernel32, "LoadLibraryW")` 拿到 LoadLibrary 的地址，`CreateRemoteThread` 在目标进程里起一个线程、以刚写的路径为参数调用 LoadLibraryW，最后 `WaitForSingleObject` 等 10 秒看退出码非零即成功。

这里有个**位宽陷阱**：64 位服务器进程里拿到的 `LoadLibraryW` 地址是 64 位 kernel32 里的，注入到 32 位目标进程里根本无效。所以 `ProcessInjector` 在注入前先用 `IsWow64Process` 判断目标是不是 32 位，如果跟自己位宽不一致，就**不走自己的注入路径，而是 spawn 一个位宽匹配的助手 exe**（`InjectViaHelper`）。这个助手就是 `WpfVisualTreeMcp.InjectorHelper`，编译成 `PlatformTarget=x86` 的 32 位 .NET 8 控制台（见 `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.InjectorHelper\WpfVisualTreeMcp.InjectorHelper.csproj`），它接收 `--pid` 和 `--dll`，调同一个 `ProcessInjector.InjectBootstrapper` 在正确的位宽下做 LoadLibrary 调用，然后退出码汇报结果。这就是 v0.6.0 引入的「64 位服务器能注入 32 位 WPF 应用」能力。注意目前只做了 x86 助手（解决最常见的 64→32 场景），反向的 32 位服务器注入 64 位目标还没做。

原生 bootstrapper 是 `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Bootstrapper\WpfInspectorBootstrapper.cpp`，一个 vcxproj 编出来的原生 DLL（Win32 和 x64 两份，输出到 `build\{Win32,x64}\Release\`）。它被 LoadLibrary 加载时，`DllMain` 的 `DLL_PROCESS_ATTACH` 起一个新线程（避免跟 loader lock 死锁），睡 100ms 让进程稳定，然后**判断目标进程是哪种 CLR**：

- 如果进程里没有 `coreclr.dll`，是 .NET Framework。走 `InitializeInspectorFramework`：用 `CLRCreateInstance` + `EnumerateLoadedRuntimes` 找到已加载的运行时，拿到 `ICLRRuntimeHost`，然后 `ExecuteInDefaultAppDomain` 调 `WpfVisualTreeMcp.Inspector.InspectorService.Initialize(string)`，参数是当前 PID。
- 如果有 `coreclr.dll`，是 .NET 5+/8+。走 `InitializeInspectorCoreCLR`：进程里已经有 `hostfxr.dll`，直接 `GetProcAddress` 拿到 `hostfxr_initialize_for_runtime_config` 等函数指针，用 Inspector 旁边那个 `.coreclr.runtimeconfig.json` 初始化 hostfxr 上下文，拿到 `load_assembly_and_get_function_pointer` 委托，加载 net8.0-windows 版的 Inspector，解析出 `InitializeUnmanaged`（签名是组件入口 `(IntPtr args, int size)`），把 PID 当 4 字节 int 传进去调用。

两条路最后都落到 Inspector 的初始化上。runtimeconfig 那个文件（`G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\WpfVisualTreeMcp.Inspector.coreclr.runtimeconfig.json`）特意把 `rollForward` 设成 `LatestMajor`、framework 指向 `Microsoft.WindowsDesktop.App 6.0.0`，这样不管目标实际跑的是 .NET 6/7/8 都能向前滚动兼容。

发布布局也值得一提（见 `WpfVisualTreeMcp.Server.csproj` 里那一大堆 `<None Include>`）。Server 工程在构建/发布时会把各种位宽的 bootstrapper、对应位宽的 Inspector、InjectorHelper.exe、coreclr 子目录都按 `native\{x64,x86}\` 和 `native\{x64,x86}\coreclr\` 的结构拷到输出目录，让运行时按位宽找得到对应文件。`ProcessInjector.GetBootstrapperDllPath` 和 `GetHelperExePath` 会按「发布布局 → 源码相对路径」的顺序找。

### 第三层：Inspector（住进目标进程里的探查核心）

Inspector 的入口是 `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\InspectorService.cs`。它有两个静态入口对应上面两条注入路径：`Initialize(string processIdString)` 给 Framework 走 `ExecuteInDefaultAppDomain` 用，`InitializeUnmanaged(IntPtr args, int sizeBytes)` 给 CoreCLR 走 hostfxr 用。两个最后都调 `Initialize(int processId)`，用双检锁保证只初始化一次。

`InspectorService` 构造时把一干协作类都 new 出来：`TreeWalker`、`PropertyReader`、`BindingAnalyzer`、`ElementHighlighter`、`PropertyWatcher`、`ResourceInspector`、`ControlInteractor`，再加一个 `IpcServer`。这些就是真正干活的模块。`HandleRequest` 是个 switch，把请求类型字符串分发到对应的 handler 方法。每个 handler 的模式都差不多：解析请求里的 handle，调 `TreeWalker.ResolveHandle` 拿回真实的 `DependencyObject`，再用对应模块读数据、拼 JSON。

整个请求处理最关键的一段在 `HandleRequestAsync`。因为 WPF 是单线程 STA 模型，所有可视树操作都必须在 UI Dispatcher 线程上跑，而命名管道的读取是另一条线程。它的做法是：先 `Task.Run` 跳出管道线程，再在里面调 `Application.Current.Dispatcher.Invoke(...)` 同步切到 UI 线程，超时给 10 秒。这里**故意用同步 `Invoke` 而不是 `InvokeAsync`**（代码注释写得很明白），是为了避免潜在的死锁。如果 Dispatcher 忙或卡住，10 秒后抛 `TimeoutException`，返回一条「UI thread is busy」的错误而不是让调用方无限等。

`IpcServer`（`G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\IpcServer.cs`）是 Inspector 这侧的命名管道服务端。管道名同样是 `wpf_inspector_{pid}`，每接一个连接就循环读「一行请求、回一行响应」。这里有个**跟服务器侧不对称的实现细节**：Inspector 用的是**直接字节 I/O**（`ReadAsync` 一个 4096 字节缓冲、自己拼到遇到 `\n`），而不是 `StreamReader`/`StreamWriter`。代码注释解释了原因——.NET Framework 4.8 上 `StreamReader`/`StreamWriter` 包 `NamedPipeServerStream` 会死锁。另外它还会在解析前**剥掉 UTF-8 BOM**（`0xEF 0xBB 0xBF` / `﻿`），避免反序列化炸掉。所有异常和调试信息都写到 `%TEMP%\WpfInspector_Debug.log`，方便排查。

Inspector 侧还维护一份调试日志写到 `%TEMP%\WpfInspectorBootstrapper.log`（bootstrapper 那侧）和 `%TEMP%\WpfInspector_Debug.log`（Inspector 这侧）。

## 几个具体的关键流程

### 元素 handle：跨调用的稳定引用

整个系统让模型「指着」某个元素的方式是 handle 字符串。规则在 `TreeWalker.GetOrCreateHandle` 里：第一次遇到一个 `DependencyObject`，就分配一个 `elem_{计数:X8}` 格式的 handle（比如 `elem_00000052`），存进 `_handleCache` 这个 `Dictionary<DependencyObject, string>`；之后再遇到同一个元素就直接复用。所有读属性、读绑定、点击、截图的工具都靠这个 handle 定位元素，`ResolveHandle` 反查字典拿回对象。

这套 handle 有几个特性需要记住：它只在**当前 Inspector 会话内有效**，也就是目标 WPF 应用还活着、Inspector 还在内存里就行；目标应用一重启，所有 handle 全部失效；handle 找不到时工具会返回明确错误（不会悄悄降级），错误信息还会提示「用 `wpf_find_elements` 拿个新 handle 再试」。CLI 文档里也强调了这点——多次 CLI 调用之间 handle 之所以还能用，是因为 handle 存在目标进程的 Inspector 里，跟 CLI 调用本身是不是无状态无关。

### 读可视树：VisualTreeHelper + AdornerLayer + Popup

`TreeWalker`（`G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\TreeWalker.cs`）是探查的中枢。`WalkVisualTree` 递归遍历，每个节点输出 `handle`、`typeName`（全名）、可选的 `name`（`x:Name`）、`depth`、`children`，最后带上 `totalElements` 和 `maxDepthReached`。JSON 是**手写 StringBuilder 拼的**，不是用序列化器——估计是为了性能和 .NET Framework 4.8 上 `System.Text.Json` 的限制。

它 traverse 子节点的方式有个亮点，集中在 `GetAllVisualChildren`：除了 `VisualTreeHelper.GetChild` 拿标准可视子节点，还会显式枚举 `AdornerLayer` 上的 adorners（比如 Fluent.Ribbon 的 Backstage 就挂在 adorner 上，纯 VisualTreeHelper 看不到），以及钻进 `Popup` 的独立可视树（Popup 的内容是另一棵树）。这就是 README 里强调的「AdornerLayer 和 Popup 遍历」能力，对调试复杂第三方控件很关键。

深度默认 25、上限 100，由工具层和 Inspector 共同钳制。没指定 `root_handle` 时，根节点取 `Application.Current.MainWindow`，没有就取第一个可见窗口（多窗口应用的常见情况），最后兜底取任意一个窗口。找元素（`FindElements`）如果不给 root，会**跨所有打开的窗口**搜，这点对「我不知道按钮在哪个窗口」的场景很友好。深搜 `FindElementsDeep` 则不限结果数（上限 10 万），但强制要求至少给 `type_name` 或 `element_name`，避免把整棵树吐出来。

### 绑定和绑定错误

`BindingAnalyzer`（`G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\BindingAnalyzer.cs`）负责这一块。读绑定靠 `TypeDescriptor.GetProperties` 枚举所有属性，对每个属性用 `DependencyPropertyDescriptor` 判断是不是依赖属性，再分别尝试 `BindingOperations.GetBindingExpression`（单绑定）和 `GetMultiBindingExpression`（多绑定）。输出的 JSON 信息很全：path、source（区分 Source 对象、RelativeSource、ElementName、DataContext）、mode、updateTrigger、converter 及其参数、StringFormat、FallbackValue、TargetNullValue、IsAsync、绑定状态（Active/PathError/Detached 等）、当前值。MultiBinding 还会展开每个子绑定单独报状态。

绑定错误的捕获是另一套机制，靠 `PresentationTraceSources.DataBindingSource`。`StartCapturingErrors` 往这个 trace source 挂一个自定义 `BindingErrorTraceListener`，把 Switch 级别设到 `Warning`。监听器把 trace 输出按行收集，每行用正则解析出错误类型（SourceNotFound、PathError、ConversionError、ValidationError、UpdateSourceError 等）、绑定 path、目标元素类型和名字、目标属性，塞进一个最多 1000 条的环形列表。模型可以 `wpf_get_binding_errors` 拉出来、`wpf_clear_binding_errors` 清空（比如测试某场景前清一下基线）。这个能力对「绑定写错了但应用没崩」这类隐形 bug 特别有用。

`GetDataContext` 还会沿可视树往上走，把 DataContext 的继承链打出来，每一层标明它的 DataContext 类型、值来源（Local/Style/Inherited 等）、是不是这一层显式设置的。这正好对应「binding path 写错了因为 DataContext 不是我以为的那个类型」这种最常见排错场景。

### 交互：点击、填字、发快捷键

`ControlInteractor`（`G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\ControlInteractor.cs`）是三个状态修改工具的后端。它的设计哲学是**默认走 UI Automation、可选走真实 OS 输入**。

点击默认走 `AutomationClick`：用 `UIElementAutomationPeer.CreatePeerForElement` 拿元素的 automation peer，再依次尝试 `IInvokeProvider`（按钮、菜单项）、`IToggleProvider`（复选框、单选框）、`ISelectionItemProvider`（列表项、Tab 项）、`IExpandCollapseProvider`（Expander、ComboBox）。这种方式的好处是不动鼠标、不需要窗口焦点、会触发正规事件。如果元素一个 pattern 都不支持，就降级到 `SyntheticMouseClick`——手动 raise 一组鼠标路由事件（PreviewDown/Down/PreviewUp/Up），算尽力而为。传 `physical=true` 则走 `PhysicalClick`：算出元素屏幕中心点，`SetCursorPos` + `mouse_event` 真点一下，会把窗口拉到前台、移动光标。

填字默认走 `AutomationSetText`：先试 `IValueProvider.SetValue`（只读会抛错），失败降级到直接设 `TextBox.Text` / `PasswordBox.Password`，再不行反射找一个可写的 string `Text` 属性（兜住很多第三方控件）。`physical=true` 则聚焦元素、先 Ctrl+A + Delete 清空、再用 `SendInput` 配 `KEYEVENTF_UNICODE` 逐字符敲进去，支持完整 BMP。

发快捷键 `SendKeys` 接受 `Ctrl+S`、`Alt+F4`、`Ctrl+Shift+F5` 这种串，由 `KeyComboParser` 解析成「修饰键数组 + 主键」，可选聚焦某个元素，否则发给当前焦点。修饰键按下、主键按一下、修饰键反序释放，模拟人手的节奏。键码映射支持 A-Z、0-9、F1-F12、Enter/Esc/Tab/Space 等。

所有交互方法返回一个 `InteractionOutcome`，带 `Method`（Invoke/ValueProvider.SetValue/Physical 等）和 `Detail`，让模型能看到「我这次点击到底是怎么完成的」。

### 截图

`ScreenshotCapture`（`G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\ScreenshotCapture.cs`）用 `RenderTargetBitmap` + `VisualBrush` 渲染元素。它先取 `VisualTreeHelper.GetDescendantBounds`，空的话退回 `ActualWidth/ActualHeight`；通过 `PresentationSource.FromVisual` 拿 DPI 算出真实像素尺寸；超过 `max_width`/`max_height`（默认 1920×1080）就按比例缩；用 `DrawingVisual` + `VisualBrush` 渲（能正确处理带变换的元素）；编成 PNG、base64 返回。这套手法能正确处理变换和非零偏移，比直接 RenderTargetBitmap 渲元素本身稳。最终在服务器侧包成 MCP `ImageContentBlock`，模型能直接看到图。

## IPC 消息契约

服务器和 Inspector 共用一份契约，在 `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Shared\Ipc\IpcMessages.cs`。所有请求继承 `IpcRequest`（带 `RequestId` 和抽象 `RequestType` 字符串），所有响应继承 `IpcResponse`（带 `Success`、`Error`）。一共 18 个请求/响应对，对应 18 种操作（GetVisualTree、FindElements、GetBindings、ClickElement、SetText、SendKeys、CaptureScreenshot 等）。序列化在 `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Shared\Ipc\IpcSerializer.cs`：请求包成 `{type, data}` 信封，响应用具体类型序列化。`Shared` 工程双目标 net8.0 和 net48（net48 上要引 `System.Text.Json` NuGet 包），这样服务器和 Inspector 都能引用同一份契约。

`Shared.Models` 下还有一组结果类型（`VisualTreeResult`、`ElementPropertiesResult`、`FindElementsResult` 等），是服务器侧解析完 Inspector 返回的 JSON 后给工具方法用的强类型包装。

## 怎么连上目标进程、完整调用链

把上面拼起来，一次「模型想知道某个按钮的属性」的完整流程是这样：

1. 模型调 `wpf_list_processes`，`ProcessManager` 枚举进程、筛 WPF，返回列表。
2. 模型调 `wpf_attach(process_id=1234, auto_inject=true)`。`ProcessManager` 检查 1234 里有没有 Inspector；没有就调 `ProcessInjector.InjectIntoProcess`，后者按位宽选 x64 或 x86 bootstrapper（位宽不匹配时 spawn `WpfInjectorHelper.exe`），CreateRemoteThread 把 bootstrapper LoadLibrary 进去；bootstrapper 的 DllMain 起新线程，按 coreclr.dll 在不在选 Framework 或 CoreCLR 路径，加载对应位宽和 TFM 的 Inspector，调它的 `Initialize`/`InitializeUnmanaged`；Inspector 起来后构造 `IpcServer` 监听 `wpf_inspector_1234`。服务器侧 `ProcessManager` 轮询这个管道出现，标记 attach 成功。
3. 模型调 `wpf_find_elements(type_name="Button")`。`WpfTools.WpfFindElements` → `NamedPipeBridge.FindElementsAsync` → 连管道发 `FindElements` 请求 → Inspector 的 `IpcServer` 收到、`HandleRequestAsync` 切到 UI Dispatcher → `TreeWalker.FindElements` 跨所有窗口搜、给每个匹配元素分配 handle → 拼 JSON 回来 → 服务器解析成 `FindElementsResult`。
4. 模型拿到 `elem_00000052`，调 `wpf_get_element_properties(element_handle="elem_00000052")`。同样的链路，Inspector 那侧 `PropertyReader.GetProperties` 枚举依赖属性、读值和值来源。
5. 模型想点这个按钮，调 `wpf_click_element(element_handle="elem_00000052")`，Inspector 那侧 `ControlInteractor.Click` 走 UI Automation invoke。

整个链路里，handle 是贯穿前后的引用凭证，命名管道是唯一的跨进程通道，UI Dispatcher 是所有可视树操作的关卡。

## 怎么构建和运行

构建就是标准的 `dotnet build`，但有几点要注意：

- **原生 bootstrapper 要先用 C++ 编译**。它是 vcxproj，`dotnet build` 不会顺带编它。得用 VS 2022 或 MSBuild 编 `WpfVisualTreeMcp.Bootstrapper.vcxproj`，产出 Win32 和 x64 两份 Release 的 `WpfInspectorBootstrapper.dll`，落到 `build\{Win32,x64}\Release\`。Server 工程的 `<None Include>` 靠 `Condition="Exists(...)"` 把它们拷进输出目录；没编过就只是不拷，不会报错，但自动注入会失败。
- 一次完整构建顺序大致是：先编 bootstrapper（C++），再 `dotnet build -c Release WpfVisualTreeMcp.sln`。Inspector 双目标会同时产出 net48 和 net8.0-windows 两份；InjectorHelper 编成 win-x86；Server 把这些都按 `native\{x64,x86}\[coreclr\]` 的布局打包好。
- 发布用 `dotnet publish src\WpfVisualTreeMcp.Server\WpfVisualTreeMcp.Server.csproj -c Release -o .\publish`，输出就是可以整体拷贝部署的目录。

接入 MCP 客户端有两种方式。最省事的是**自宿主模式**：你自己的 WPF 应用引一个 Inspector 的项目引用，在 `App.OnStartup` 里调 `InspectorService.Initialize(Process.GetCurrentProcess().Id)`——SampleWpfApp（`G:\Projects\WPFVisualTreeMcp\samples\SampleWpfApp\App.xaml.cs`）就是这么干的。这样应用启动时 Inspector 就在了，服务器 attach 时直接发现「已加载」，不用注入。另一种是**自动注入模式**：任意正在跑的 .NET Framework 或 .NET 5+ WPF 应用，模型调 `wpf_attach(auto_inject=true)`，服务器自动注入。前者最稳，适合开发测试自己的应用；后者最灵活，适合探查别人家的应用。

MCP 客户端配置就是把 `.mcp.json` 或 `claude mcp add` 指向那个 `WpfVisualTreeMcp.Server.exe`，路径用绝对路径、用正斜杠。配完重启客户端。

## 几点值得注意或容易踩的地方

写完做个提醒，做后续研究时这几条值得留心：

**文档和代码不完全同步。** `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Injector\README.md` 还把注入器说成「stub implementation」、把 CreateRemoteThread 列为「未来方案」，但实际 `ProcessInjector.cs` 早已完整实现并带跨位宽助手了。`docs\ARCHITECTURE.md` 也比较旧，比如它说「Inspector 是 .NET Framework 4.8 库」「只有 .NET Framework WPF 能被探查」，而代码里 Inspector 已经双目标、CoreCLR 注入路径也做完了。看代码为主、文档为辅。

**两边命名管道 I/O 实现不一样。** 服务器侧用 `StreamReader`/`StreamWriter`，Inspector 侧用直接字节 I/O。原因是 .NET Framework 4.8 上 StreamReader 包管道会死锁，所以只在 Inspector 那侧避开。这不是 bug，是有意的兼容处理，但读代码时会困惑一下。

**stdout 必须干净是硬约束。** MCP stdio 协议的 JSON-RPC 帧走 stdout，任何杂散字节都会破坏协议。所以 Serilog 配置里 `standardErrorFromLevel` 设成 `Verbose`（等于所有日志都进 stderr），`Program.cs` 里也专门清掉了默认 logging provider。这点在做任何服务器侧改动时都要守着。

**Inspector 的 handle 缓存没有失效机制。** 它是个普通的 `Dictionary<DependencyObject, string>`，键是元素对象引用。如果 WPF 应用里某个元素被回收了、对应的 `DependencyObject` 被 GC 了，字典里那条理论上会失效（键对象没了），但代码没显式处理；不过实际使用中只要目标窗口结构稳定、元素引用一直被可视树持有，这不会是问题。

**安全模型偏宽松。** 注入走的是 `OpenProcess` 全权限 + CreateRemoteThread，需要足够权限（通常要管理员或同一用户会话）；命名管道没设显式 ACL（`NamedPipeServerStream` 用的是默认安全描述符）。`docs\ARCHITECTURE.md` 里提到「管道有合适 ACL、只有服务器能连」，实际代码里没这么做。在受控的开发机上没问题，但在多用户或不可信环境里要注意。

**测试覆盖偏单元、偏路径解析。** `tests\WpfVisualTreeMcp.Tests` 下有 `ProcessInjectorTests`、`ProcessManagerTests`、`SharedModelsTests`、`McpServerTests`，主要测的是路径解析、进程判断、序列化这些好测的部分；真正的注入、IPC、可视树读取这类重的流程没有自动化集成测试，得靠 SampleWpfApp 手动验。这跟它涉及进程注入、UI 线程、外部应用的性质有关——不好写无副作用的测试。

## 相关源码（绝对路径）

按模块归类，方便核对：

**MCP 服务器层**
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\Program.cs` — 服务器入口、stdio/CLI 模式分流、Serilog 配置、DI 注册。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\WpfTools.cs` — 全部 20 个 MCP 工具定义，`[McpServerTool]` 标注。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\Cli\CliRunner.cs` — 一次性 CLI 前端，20 个子命令、参数解析、JSON 输出。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\Services\ProcessManager.cs` — 进程发现、attach、注入编排、等管道就绪。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\Services\NamedPipeBridge.cs` — 服务器侧命名管道客户端、请求发送和响应解析。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\Services\IProcessManager.cs` — `WpfProcessInfo`、`InspectionSession`、`IProcessManager` 接口。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\Services\IIpcBridge.cs` — IPC 桥接口。

**注入管线**
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Injector\ProcessInjector.cs` — CreateRemoteThread + LoadLibrary 注入、位宽检测、跨位宽 spawn 助手、各种 P/Invoke。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.InjectorHelper\Program.cs` — 32 位助手 exe，`--pid`/`--dll` 调 `InjectBootstrapper`。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Bootstrapper\WpfInspectorBootstrapper.cpp` — 原生 bootstrapper，Framework（ExecuteInDefaultAppDomain）和 CoreCLR（hostfxr）两条初始化路径。

**Inspector 探查核心**
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\InspectorService.cs` — 入口、双检锁初始化、请求分发、Dispatcher.Invoke 切 UI 线程。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\IpcServer.cs` — 目标进程侧命名管道服务端、直接字节 I/O、BOM 剥除。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\TreeWalker.cs` — 可视树/逻辑树遍历、handle 分配与解析、AdornerLayer/Popup 遍历、找元素、XAML 导出。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\PropertyReader.cs` — 依赖属性枚举、值与值来源、布局信息。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\BindingAnalyzer.cs` — 单绑定/MultiBinding 解析、DataContext 继承链、绑定错误捕获（trace listener + 正则分类）。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\ControlInteractor.cs` — 点击/填字/发快捷键，UI Automation 默认 + OS 输入降级，键码解析。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\ScreenshotCapture.cs` — RenderTargetBitmap + VisualBrush 截图、DPI 感知、超限缩放。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\ResourceInspector.cs` — 资源字典枚举（含 MergedDictionaries）、样式与 trigger 解析。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\ElementHighlighter.cs` — 红框覆盖窗口高亮元素。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\PropertyWatcher.cs` — 依赖属性变化监视、通知事件。

**契约与模型**
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Shared\Ipc\IpcMessages.cs` — 18 个请求/响应对、通知类型。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Shared\Ipc\IpcSerializer.cs` — 请求信封、响应序列化。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Shared\Models\*.cs` — 服务器侧用的强类型结果模型。

**构建配置**
- `G:\Projects\WPFVisualTreeMcp\WpfVisualTreeMcp.sln`、`G:\Projects\WPFVisualTreeMcp\Directory.Build.props` — 解决方案和通用构建属性。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Server\WpfVisualTreeMcp.Server.csproj` — 发布布局（`native\{x64,x86}\[coreclr\]` 文件拷贝规则）。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Inspector\WpfVisualTreeMcp.Inspector.csproj` — Inspector 双目标 net48;net8.0-windows。
- `G:\Projects\WPFVisualTreeMcp\src\WpfVisualTreeMcp.Bootstrapper\WpfVisualTreeMcp.Bootstrapper.vcxproj` — 原生 DLL 的 Win32/x64 配置。
