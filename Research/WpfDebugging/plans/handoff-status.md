# WPF 调试机制实现 · 会话接力状态

本文档为跨会话接力用，记录 McpServerForDevEnv 的 WPF 调试机制实现进度。新会话从这里接手。

## 路径说明
本会话（新电脑，2026-06-20）Projects 根 = `C:\Users\James11\Documents\Projects\`，仓库根 = `C:\Users\James11\Documents\Projects\McpServerForDevEnv`，下面所有相对路径都相对仓库根写。上一会话根在 `G:\Projects\`，引用的 `G:\Projects\CodeAtelier\MagicTools\` 智能体已复制到本机 `C:\Users\James11\Documents\Projects\CodeAtelier\MagicTools\`（目前只复制了 VbNetDev / VbNetReviewer / God 三个，manifest.md 已就地裁剪掉不存在的条目）。

**项目文件夹重构（2026-06-21）**：6 个 WpfDebugging 项目文件夹 + 项目文件名已去掉 `McpServerForDevEnv.` 前缀（`McpServerForDevEnv.WpfDebugging.Core` → `WpfDebugging.Core`，.vbproj/.csproj 同步重命名）。**AssemblyName/RootNamespace/代码命名空间保持 `McpServerForDevEnv.WpfDebugging.*` 不变**（程序集/dll/exe 名不变，代码零改动）。下文 #19–#24 描述里的旧路径 `McpServerForDevEnv.WpfDebugging.X\...` 对应新 `WpfDebugging.X\...`。重构后全 sln build 0 错误、默认单测 217 全过、集成测试 5/5 全过。

## 新电脑环境适配（2026-06-20）
- **net472 引用程序集（已解决 MSB3644）**：新电脑没装 .NET Framework 4.7.2 Developer Pack，net472 目标报 `error MSB3644`。已在仓库根放 `Directory.Build.props`，给 `TargetFrameworkIdentifier == .NETFramework` 的项目注入 `Microsoft.NETFramework.ReferenceAssemblies 1.0.3`（PrivateAssets=all）。覆盖 McpServiceNetFx / Core / Tests + WpfDebugging.Core / Target 的 net472 目标。**别删这个文件**，删了 net472 又编译不过。
- **VsixAsync 移出 sln**：用户决定本次改造不管 McpServiceNetFx.VsixAsync、允许它编译失败。已用 `dotnet sln remove` 把它从 `McpServerForDevEnv.sln` 移除（GUID `{451C3F20-...}` 已 0 残留），vbproj 文件保留未删。
- **测试项目改 net10.0-windows（用户已定）**：本机只装 .NET 10.0.9 三个 runtime（NETCore / AspNetCore / WindowsDesktop），没有 net8。原 net8.0-windows 的 testhost 起不来（报 `You must install or update .NET`，且项目级 RollForward 对 testhost 无效）。已把 Target.Tests.csproj 和 Core.Tests.vbproj 的 TargetFramework 从 net8.0-windows 改成 net10.0-windows，testhost 直接用本机 net10 WindowsDesktop runtime，**无需任何 env var**，普通 `dotnet test` 即可。被测 Target（多目标 net472;net8.0-windows）被 net10 测试引用时 NuGet 选 net8 目标产物，net10 runtime 加载 net8 程序集兼容。
- **build/test 自检跑法**：仍按「fire-and-forget + 输出重定向到系统临时目录 `/tmp/xxx.log` + 后台跑 + 跑完 Read 日志」；输出文件 Windows 路径在 `C:\Users\James11\AppData\Local\Temp\xxx.log`。

## 任务概述
给 McpServerForDevEnv（一个 VB.NET net472 的 VS/DTE 调试 MCP 服务）增加一套 WPF 调试机制。能力面对标 chrome-devtools-mcp，但把浏览器 page 换成「被控进程 + 窗口」，远程脚本用 VB.NET 而非 JS，被控端是被控 WPF 程序内嵌的 NuGet 包（不是远程注入），主控和被控端之间走 named pipe。

整体三层 + 一条 IPC：

- 共享契约库 Core（两边引用）
- 被控端 Target（NuGet 包，在宿主进程内起 pipe server、在 UI 线程干活）
- 主控（现有 McpServiceNetFx 扩展，做协议网关 + WPF 调试 tab + 工具注册）
- named pipe：主控 ↔ 被控端的内部协议（length-prefixed JSON-RPC 风格，不是 MCP）

## 已定决策（不要改）
1. 单被控端：主控一次只连一个被控进程，不做多被控路由。
2. 不支持 VSIX：只在独立 GUI版 McpServiceNetFx 实现，VsixAsync 平行宿主不做。
3. 固定 pipe 名 + 协议版本：被控端起写死的 pipe 名 `mcpserverfordevenv.wpfdebug.v1`，主控直连这个名（连上即发现），握手时被控端回报 pid/主窗口标题/协议版本。同名撞名直接报错。
4. 脚本权限 = 被控进程权限：evaluate 的 VB.NET 脚本完全放开，不沙箱。
5. 工具常驻：WPF 工具常驻工具列表，未连接时报「未连接被控端」，不做 listChanged 推送。
6. uid 方案：被控端取 `DependencyObject.GetHashCode().ToString()` 当 uid（WPF 重写过 GetHashCode，进程内稳定单调自增），不用 ConditionalWeakTable。
7. 语言：实现代码全部 VB.NET。测试项目当初因「VB+UseWPF+TestSDK」组合在沙箱外让 vbc 崩溃改用 C#；**2026-06-21 已原地转回 VB**（当前环境 vbc 对 VB+UseWPF+TestSDK 稳定，`WpfDebugging.Target.Tests` 现是 VB 项目，build 0 错误、82 测试全过，覆盖与原 C# 等价）。

## 架构与项目结构
解决方案 `McpServerForDevEnv.sln`（仓库根）。现有项目：`McpServiceNetFx`（主宿主 GUI，VB net472）、`McpServiceNetFx.Core`（公共库，net472，含 EnvDTE）、`McpServiceNetFx.VsixAsync`（VSIX 平行宿主）、`McpServiceNetFx.Tests`。

已新增项目：

- `McpServerForDevEnv.WpfDebugging.Core`：共享契约库，VB 多目标 net472;net8.0-windows，纯 POCO+接口，零 EnvDTE 零 WPF 强依赖。含 WpfDebugProtocol（pipe 名/协议版本/Method 常量/消息类型/uid 约定注释）、SnapshotNode/WindowInfo/ScreenshotResult 模型、IWpfDebugTarget 能力接口、MessageFramer（4 字节大端长度 + UTF-8 JSON 的 framing）。
- `McpServerForDevEnv.WpfDebugging.Target`：被控端 NuGet 包，VB 多目标 net472;net8.0-windows，UseWPF=true，引用 Core。含 WpfDebugHost(Start/Stop)、WpfDebugHostConfig、WpfDebugTargetImpl(实现 IWpfDebugTarget，骨架阶段各方法 stub)、HandshakeInfo、WpfDebugPipeServer(固定名 pipe server，一连接一 Task 串行，maxNumberOfServerInstances=1 撞名报错，DispatchAsync 真异步切 UI 线程，ReadAsync 60s 空闲超时)、UidRegistry(GetHashCode→uid + Dictionary(int→WeakReference) 反查)、WpfDispatcher(只暴露异步 InvokeAsync)。
- `McpServerForDevEnv.WpfDebugging.Target.Tests`：C# net8.0-windows MSTest，测 UidRegistry/WpfDispatcher/Handshake。

待新增（后续块）：主控侧 WPF 调试模块（加进 McpServiceNetFx + .Core）。

依赖：Target → Core；Target.Tests → Target → Core；主控模块 → Core。

## 进度
- **#17 共享 Core 契约库** ✅ 完成（代码审查 + build/test 通过，17 测试过）。
- **#18 被控端包骨架** ✅ 完成（代码审查 + build/test 通过，15 测试过；含死锁修复：DispatchAsync 真异步切 UI 线程、ReadAsync 60s 超时、OkResult 类型修复）。
- **#19 被控端 list_windows + take_snapshot** ✅ 完成：上个会话实施 agent 写完代码（Target 的 WindowEnumerator / VisualTreeWalker / SnapshotFormatter / InterestingNodeFilter + WpfDebugTargetImpl 接线），本会话 VbNetReviewer 独立复核通过——10 条 checklist 全 ✅（OkResult 嵌套注释、interestingOnly 裁剪、uid 时机、UI 线程不二次切、AdornerLayer/Popup 覆盖、纯函数格式、uid 失效提示、Global.System.Windows 限定、死锁红线、单测无副作用），34 单测全过。仅 4 个 Minor 不阻塞（M1 VisualTreeWalker.GetVisualChildren 里 adorned 与 visual 重复 TryCast 可删其一、M2 ResolveName 短路语义建议加注释、M3 SnapshotFormatter 布尔字面量 "True"/"False" 约定建议加注释、M4 提醒 commit 当前改动），留 #20 实施者顺手清。
- **#20 被控端 click/fill/take_screenshot** ✅ 完成：VbNetDev 实施 + VbNetReviewer 复核通过——Target 新增 `Interaction\ElementInteractor.vb`（click 优先 AutomationPeer.Invoke 退化 RaiseEvent；fill 按控件类型分流，抽纯函数 ResolveFillStrategy 便于单测）+ `Capture\WindowCapturer.vb`（RenderTargetBitmap→Png→base64），WpfDebugTargetImpl 接好三方法（click 退化路径事件代码经核正确：Mouse.PrimaryDevice + UIElement.PreviewMouseLeftButton*/MouseLeftButton* 静态字段 + MouseButtonEventArgs 三参构造）。10 checklist 全 ✅，54 测试过（34+20）。2 个 P3 Minor 不阻塞（click 退化路径对非 Button 控件静默、WindowCapturer 内部 DpiScale 重名遮蔽），留观察/#24。
- **#21 被控端 evaluate（Roslyn VB 脚本引擎，双目标程序集加载）** ✅ 完成：VbNetDev 实施 + VbNetReviewer 复核通过——Target 新增 `Scripting\{ScriptEngine,AssemblyLoader,ScriptHost,ScriptResultSerializer}.vb`（脚本→Module+Function→VisualBasicCompilation→Emit 内存PE→AssemblyLoader 加载→反射 Execute→序列化；双目标加载运行时探测分流 DetectLoaderKind：net472 AppDomain+Assembly.Load+AssemblyResolve / net8.0 AssemblyLoadContext 反射 NonPublic+Resolving(DynamicMethod thunk)，无 #If；宿主 Application/MainWindow/ActiveWindow/Element(uid)/GetProperty/SetProperty/Log；Lazy 懒加载；软超时 Stopwatch+host 检查+IPC 60s 兜底，纯计算死循环无法硬中断为已知限制）。82 测试过（55+27）。三个专项评估全 ✅（超时软方案 trade-off 可接受、双目标加载分流正确、依赖属性精确匹配修复）。**Major M1 不阻塞**（`AssemblyLoader.CoreClrLoaderImpl.LoadFromPathImpl` 新建 ALC 未挂 Resolving→间接依赖链断，触发要 #R/间接依赖，留 #24 用 #R 场景实测并修，修法：复用 AttachResolving）+ Minor（依赖属性合法 null 当未读到、WrapScript 死代码）留 #24。
- **#22 主控 pipe client + WpfDebugProxy + 连接管理 + WPF 调试 tab** ✅ 完成：VbNetDev 实施 + VbNetReviewer 复核通过——主控 net472 新增 `WpfDebug\{WpfDebugProxy,WpfDebugResultReader,WpfDebugConnection}.vb` + `Helpers\WpfDebugConnectionMonitor.vb` + `Views\MainWindow.WpfDebug.vb`(partial) + xaml TabItem；`VisualStudioToolBase` 加 `_wpfDebugProxy`/`SetWpfDebugProxy`/`IsWpfDebugConnected`，`VisualStudioToolManager` 加 `CreateWpfDebugTools`/`ClearWpfDebugProxy` 骨架；.Core 引用 WpfDebugging.Core。8 checklist 全 ✅，两个 handoff 易错点确认正确（**OkResult 嵌套解析 response.Result("result")**、pipe 全程异步无死锁零 `.Result/.Wait()`）。McpServiceNetFx.Tests 86 测试过，新增文件零 warning（13 警告全来自既有文件）。2 个 Minor 不阻塞（`SendRequestAsync` 取消回调未 Dispose、Finally 与取消回调重复 TryRemove）留 #24。VbNetDev 顺手补了 MaintainerAgent architecture.md/features.md 的 WPF 调试连接层记录。
- **#23 主控 WPF 工具基类（WpfDebugToolBase）+ MVP 六工具注册** ✅ 完成：VbNetDev 实施 + VbNetReviewer 复核通过——主控 `Tools\WpfDebug\` 新增 `WpfDebugToolBase.vb`（ExecuteInternalAsync 抽公共逻辑：未连接检查+权限+SendRequest→GetPayload→FormatResult+被控端报错转换，3 MustOverride Method/BuildParams/FormatResult，不调 _vsTools）+ 六工具 `ListWindows`/`TakeSnapshot`/`Click`/`Fill`/`Evaluate`/`TakeScreenshot`（Method 走 WpfDebugMethods 常量、OkResult 走 GetPayload 嵌套解析、evaluate 权限 Ask 其余 Allow）；`VisualStudioToolManager` 常驻注册、未连接报错不做 listChanged。9 checklist 全 ✅，两个 handoff 易错点确认正确（六工具参数映射对照 IWpfDebugTarget 契约一一对应、OkResult 嵌套走 GetPayload **无一处直接取 response.Result**）。McpServiceNetFx.Tests 116 测试过（新增 7 文件零警告）。2 个 Minor 不阻塞（WPF 工具依赖先选 VS 实例的隐性前置、handoff 测试/警告数字偏差）留 #24 联调留意。
- **#24 端到端联调 + 编译验证** ✅ 完成：VbNetReviewer 复核通过——**整个 WPF 调试 MVP（#19–#24）完成可交付**。建 SampleHost（WPF 被控，引用 Target 起 pipe + 测试控件）+ 修 #21 M1（AssemblyLoader 间接依赖）+ IntegrationTests（不进 sln + `[TestCategory("Integration")]` 隔离，默认 dotnet test 不扫到）+ 补 architecture.md WpfDebugging 完整架构。**联调暴露并修了两个深层 bug**：①Target VisualTreeWalker 对 TextBox 内部 TextBoxLineDrawingVisual（DrawingVisual 非 UIElement）遍历崩→容错三重防线（per-node try/catch + MaxDepthHardLimit=500 + GetVisualChildren 三路各自 try/catch + Popup.Child 用 DependencyObject）；②**Core IO/MessageFramer 字节编解码 bug（被 #19–#23 掩盖 4 迭代）**——`CByte(body.Length)` body>255 溢出 + VB `Byte<<shift` 左移截断，致 >255 字节响应全失败（take_snapshot 35KB/screenshot base64），修为 `CByte((len>>N) And &HFF)` + `CInt(header(N))<<shift`，**帧格式不变 IPC 完全兼容**（主控/被控端引用同一份 MessageFramer），Core.Tests 加边界单测。独立重跑：Core.Tests 19 + Target.Tests 82 + McpServiceNetFx.Tests 116 全过，**IntegrationTests 自动化跑通 list_windows + take_snapshot + evaluate + take_screenshot**（真实 SampleHost↔主控 pipe 全链路）。sln build 0 错误，12 警告全来自既有 McpServiceNetFx（**WpfDebugging 新增代码零警告**）。3 个 Minor 已清（IntegrationTests `FindFirstEditableUid` 字段名 `"type"`→`"typeName"` + 过时注释更新 + fill/click assert 改为检查 `Error Is Nothing`，因 click/fill 是 Sub 方法被控端无 payload）。**集成测试最终 5/5 全绿**（list_windows + take_snapshot + evaluate + take_screenshot + Fill_And_Click，真实 SampleHost↔主控 pipe **六工具端到端全验证**，click/fill 真正生效）。

## 🎉 整个 WPF 调试 MVP（#17–#24）全部完成

- **默认单测 217 全过**（Core.Tests 19 + Target.Tests 82 + McpServiceNetFx.Tests 116），集成测试 5/5 全过，sln build 0 错误（12 警告全来自既有 McpServiceNetFx，WpfDebugging 新增代码零警告）。
- 被控端 Target 六能力（list_windows/take_snapshot/click/fill/take_screenshot/evaluate）+ 主控连接层（pipe client/proxy/连接监控/tab）+ 主控六工具（WpfDebugToolBase + MVP 六工具注册）+ SampleHost 被控示例 + IntegrationTests 端到端验证 + MaintainerAgent 知识缓存全部就位。
- 环境适配：`Directory.Build.props` 注入 net472 引用程序集（解决 MSB3644）、测试项目改 net10.0-windows（本机无 net8）、VsixAsync 出 sln（用户豁免）。
- #24 联调挖出两个被单测掩盖的深层 bug（VisualTreeWalker 对 DrawingVisual 强转崩 + Core MessageFramer >255 字节编解码，后者潜伏 4 迭代），均从根因修复 + 补边界单测。
- **遗留**：真实桌面 GUI 交互的深度联调（多控件场景/性能/大型可视树分页）留用户环境；VsixAsync 平行宿主不做（已定决策）。

## 工作方式约定（务必遵守）
- **普通后台 agent**：派 subagent 用 Agent 工具 + `run_in_background:true`，**不要给 name 和 team_name 参数**（给了 name 会变成 teammate / agent teams 模式，用户不要这个）。
- **实施/验证交替**：每块先派实施 agent，做完派验证 agent 独立复核（不只信实施者自述），通过才下一块。main 只调度，进度用 todo 列表驱动，不催。
- **build/test 自检**：跑 dotnet build/test 用 fire-and-forget + 输出重定向到临时文件（写系统临时目录，别写仓库根），完成后 Read 文件拿结果。**别用前台同步、别用管道符 `|`、别重定向到父进程标准流**——本环境管道会卡死/崩溃。这条要写进给 agent 的指令。
- **沙箱**：dotnet 及其子进程必须进同一个沙箱（用户已配好），vbc 才能编译；若 build 报 vbc MSB6006 无输出崩溃，先查沙箱有没有把 dotnet 子进程纳入。
- **优先内置 Read/Write/Edit/Grep/Glob**（Windows 路径格式 `G:\...`），Bash 易卡（尤其管道）；`git status` 这种快命令可用。
- **.NET 单测要写但无副作用**（不发网络、不写文件、不启进程、不写注册表、不碰真 UI 线程）。new 一个 DependencyObject 当纯内存对象测 uid/映射不算副作用。真实 pipe/Dispatcher/可视树遍历没法无副作用测的，标注集成验证留到 #24。
- **中文回答地道自然**，别翻译腔、少堆表格和行号。
- subagent 的 model 继承父（glm-5.2），Agent 工具调用时省略 model 参数。

## 关键技术约定与坑
- uid = `DependencyObject.GetHashCode().ToString()`；UidRegistry 维护 `Dictionary(Of Integer, WeakReference(Of DependencyObject))` 反查，GetOrCreateUid/Resolve/Scavenge/Clear 加锁线程安全。
- **OkResult 约定**：WpfDebugPipeServer 把每个方法返回值统一包成 `Result = { "result": <JToken> }`。所以 list_windows 返回的 IList(Of WindowInfo) 序列化后，主控拿到的是 `response.Result["result"]`（嵌在 "result" 键下），主控 client 解析要按这个嵌套取，不能直接当值。#22 实现主控 client 时注意。
- 被控端 WPF 类型一律用 `Global.System.Windows.*` 完整限定（Target 的 RootNamespace=McpServerForDevEnv.WpfDebugging.Target，子命名空间 Windows 会和 System.Windows 歧义）。
- IPC framing：4 字节大端长度 + UTF-8 JSON，用 Core 的 MessageFramer。序列化用 Newtonsoft 13.0.4（全链统一，别引入 System.Text.Json）。
- **死锁红线**：被控端全程异步（WpfDispatcher 只暴露 InvokeAsync，绝无 Dispatcher.Invoke 同步/.Result/.Wait()/.GetAwaiter().GetResult()）；IWpfDebugTarget 的实现方法由 DispatchOnUiThread 已切到 UI 线程调用，**方法体不要再二次切线程**（避免 InvokeAsync 套 InvokeAsync），直接同步遍历/操作。
- 撞名：WpfDebugPipeServer 用 maxNumberOfServerInstances=1，第二个被控端 Start 同名 pipe 抛 IOException，单被控语义直接报错。
- 测试项目用 C#（理由见已定决策 7）；新测试加到现有 Target.Tests（C#）。

## 当前接手点：#19 被控端 list_windows + take_snapshot
在被控端 Target 实现 list_windows 和 take_snapshot，替换 WpfDebugTargetImpl 里的 stub：

- list_windows：枚举 Application.Current.Windows（必要时含所有 HwndSource 根窗口），产 IList(Of WindowInfo)。每个 WindowId 走 UidRegistry.GetOrCreateUid(window)（Window 也是 DependencyObject）；Title/IsMain/Handle 填好。
- take_snapshot：对指定 windowId（Nothing=活动窗口）的可视树用 VisualTreeHelper 遍历，maxDepth(Integer?,Nothing=不限)、interestingOnly(Boolean,默认 False) 可选。产 SnapshotNode 树：Uid=GetOrCreateUid、TypeName=GetType().Name、Name=x:Name/Name/Content/Text 摘要、Properties 挑有调试价值的（IsEnabled/Visibility/IsVisible/IsFocused/IsHitTestVisible 等）。同时产对标 a11y 的文本格式：缩进每层 2 空格，每节点一行 `uid=<id> <类型> "<名称>" <属性...>`，布尔属性直接印 key、标量 key="value"（规则参考 Research/WpfDebugging/docs/take-snapshot-a11y快照格式.md）。**文本格式化抽成纯函数**便于单测。
- uid 反查用 UidRegistry.Resolve；失效（对象不在树/已 GC）返回明确 WpfDebugError 提示重新 snapshot。
- 树遍历含 AdornerLayer/Popup（参考 Research/WpfDebugging/docs/WPFVisualTreeMcp-分析.md）。

提醒（验证 agent 给的，务必遵守）：

1. OkResult 把返回值包成 Result={"result":<JToken>}，方法体只管返回 IList(Of WindowInfo)/SnapshotNode，封装分派层做；但要在代码注释里写明这个「result 键嵌套」约定。
2. interestingOnly 是 Boolean 非空，分派层默认 False，处理 False(全树)/True(裁纯装饰节点) 两种。
3. uid 时机：遍历每个 DependencyObject 调 GetOrCreateUid 填 Uid；list_windows 的 WindowId 也走 GetOrCreateUid。
4. UI 线程：方法体由 DispatchOnUiThread 已切 UI 线程调用，别再二次切线程，直接同步遍历。
5. 自检 build/test 用 fire-and-forget + 文件重定向，别带管道。

单测（无副作用，加到 Target.Tests C#）：文本格式化纯函数、interestingOnly 裁剪、树→SnapshotNode 映射（用 fake DependencyObject 节点测）。真实可视树遍历标注集成验证留 #24。

红线：只动 Target + 它的测试；不碰主控；Core 契约别改（文本格式约定加注释即可）。

## 参考文档（仓库内）
- `Research/WpfDebugging/plans/rough-plan.md`：完整设计（架构、概念模型、IPC、工具集、脚本引擎、主控集成、7 条技术困难）。
- `Research/WpfDebugging/plans/req-analysis.md`：需求分析 + 已定决策。
- `Research/WpfDebugging/docs/`：6 篇分析（chrome-devtools 工具定义、a11y 快照格式、McpServerForDevEnv 现状、WPFVisualTreeMcp、Winwright、VBScriptDotNet 编译与程序集加载）。

## 仓库根临时文件（可删）
build19.log、test19.log、test19b.log、test19c.log、tests-build.log、wpftest.log 都是 build/test 自检重定向产物，堆在仓库根，接手时可删。
