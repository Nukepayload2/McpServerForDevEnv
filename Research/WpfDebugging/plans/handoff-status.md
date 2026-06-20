# WPF 调试机制实现 · 会话接力状态

本文档为跨会话接力用，记录 McpServerForDevEnv 的 WPF 调试机制实现进度。新会话从这里接手。

## 路径说明
上一个会话的「Projects 根」在 `G:\Projects\`。新会话的 Projects 根在别处，本文档里所有 `G:\Projects\...` 都替换成新会话的实际 Projects 根。仓库根是 `McpServerForDevEnv`（即 `<新 Projects 根>\McpServerForDevEnv`），下面所有相对路径都相对这个仓库根写。

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
7. 语言：实现代码全部 VB.NET。测试项目因为「VB+UseWPF+TestSDK」组合在沙箱外会让 vbc 崩溃，改用 C# 测试项目（沿用 `McpServerForDevEnv.WpfDebugging.Target.Tests`，C#）；沙箱修好后 vbc 已恢复，但 C# 测试已建好，沿用即可。

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
- **#19 被控端 list_windows + take_snapshot** 🔄 进行中：上一会话的实施 agent 做到一半，可能在 Target 写了部分代码、留了 build19.log/test19*.log 等临时日志（堆仓库根，可删）。接手先 `git status` + 读 `McpServerForDevEnv.WpfDebugging.Target/` 判断 list_windows/take_snapshot 是否已实现，再接续或重做。任务范围和提醒见下「当前接手点」。
- **#20 被控端 click/fill/take_screenshot** ⏳ 未开始。
- **#21 被控端 evaluate（Roslyn VB 脚本引擎，双目标程序集加载）** ⏳ 未开始。最重的一块。
- **#22 主控 pipe client + WpfDebugProxy + 连接管理 + WPF 调试 tab** ⏳ 未开始。
- **#23 主控 WPF 工具基类（WpfDebugToolBase）+ MVP 六工具注册** ⏳ 未开始。
- **#24 端到端联调 + 编译验证** ⏳ 未开始。

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
