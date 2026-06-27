---
name: Architecture
description: 项目架构维护子智能体，描述 McpServerForDevEnv 的三层架构、项目职责和文件夹结构
input:
  - query: 架构相关问题
output:
  - 项目架构说明、项目职责划分、核心文件夹结构
---

# 项目架构维护子智能体

## 职责

维护 McpServerForDevEnv 项目的架构知识，包括三层架构设计、各项目职责划分、核心文件夹结构和依赖关系。

## 项目架构概览

McpServerForDevEnv 采用三层架构设计，将 UI 层、核心业务逻辑层和 Visual Studio 集成层分离：

```
┌─────────────────────────────────────────────────────────┐
│                    表现层 (Presentation)                  │
│  ┌──────────────────┐      ┌─────────────────────────┐  │
│  │ McpServiceNetFx  │      │ McpServiceNetFx.VsixAsync│  │
│  │  (独立 WPF UI)    │      │   (VS 插件 UI)           │  │
│  └──────────────────┘      └─────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│                  核心业务逻辑层 (Core)                     │
│              ┌──────────────────────────┐                │
│              │ McpServiceNetFx.Core      │                │
│              │  (MCP 协议实现)            │                │
│              └──────────────────────────┘                │
└─────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│               Visual Studio 集成层 (VS Integration)        │
│              (通过 RPC 与 VS 实例通信)                     │
└─────────────────────────────────────────────────────────┘
```

## 项目职责划分

### 1. McpServiceNetFx - 独立服务管理器

**技术栈**: VB.NET + .NET Framework + WPF + WPF-UI

**主要职责**:
- 提供独立的 WPF 应用程序界面
- 枚举和管理多个 Visual Studio 实例
- 监控 VS 实例的运行状态
- 配置和管理 MCP 服务
- 权限控制配置
- 日志显示和服务状态监控

**核心文件夹结构**:

```
McpServiceNetFx/
├── Application.xaml.vb           # 应用程序入口
├── AssemblyInfo.vb               # 程序集信息
├── Helpers/                      # 辅助模块
│   ├── PersistenceModule.vb      # 配置持久化
│   ├── UtilityModule.vb          # 通用工具
│   ├── VBHost.vb                 # VB 宿主相关
│   ├── VisualStudioEnumerator.vb # VS 实例枚举
│   ├── VisualStudioMonitor.vb    # VS 实例监控
│   └── WpfDebugConnectionMonitor.vb # WPF 调试连接监控（#22 新增）
├── Views/                        # XAML 视图
│   ├── MainWindow.xaml           # 主窗口（含 WPF Debug tab，#22 新增）
│   ├── MainWindow.xaml.vb        # 主窗口代码后置
│   ├── MainWindow.Logging.vb     # 日志功能
│   ├── MainWindow.McpService.vb  # MCP 服务管理
│   ├── MainWindow.Permissions.vb # 权限配置
│   ├── MainWindow.VsInstances.vb # VS 实例管理
│   ├── MainWindow.WpfDebug.vb    # WPF 调试 tab（#22 新增）
│   ├── CustomMessageBox.xaml     # 自定义消息框
│   └── UserMessageWindow.xaml    # 用户消息窗口
└── McpServiceNetFx.vbproj        # 项目文件
```

**UI 功能区**:
- VS 实例选择和监控
- MCP 服务启动/停止控制
- 权限级别配置（Allow/Ask/Deny）
- 实时日志显示
- 连接状态监控

### 2. McpServiceNetFx.Core - MCP 核心实现

**技术栈**: VB.NET + .NET Framework

**主要职责**:
- 实现 MCP 协议规范
- 提供 HTTP 服务接口
- 定义和实现 MCP 工具
- 处理权限控制逻辑
- 管理 VS RPC 通信
- 提供数据模型定义

**核心文件夹结构**:

```
McpServiceNetFx.Core/
├── Mcp/                          # MCP 协议核心
│   ├── IMcpLogger.vb             # 日志接口
│   ├── IMcpPermissionHandler.vb  # 权限处理接口
│   ├── McpExceptions.vb          # MCP 异常定义
│   ├── McpService.vb             # MCP 服务主类
│   ├── VisualStudioMcpHttpService.vb  # HTTP 服务
│   └── VisualStudioMcpTools.vb   # 工具集合定义
├── Tools/                        # MCP 工具实现
│   ├── VisualStudioToolBase.vb   # VS 工具基类
│   ├── VisualStudioToolManager.vb # 工具管理器
│   ├── VisualStudioTools.vb      # 工具注册
│   ├── BuildSolutionTool.vb      # 构建解决方案
│   ├── BuildProjectTool.vb       # 构建项目
│   ├── GetErrorListTool.vb       # 获取错误列表
│   ├── GetSolutionInfoTool.vb    # 获取解决方案信息
│   ├── GetActiveDocumentTool.vb  # 获取活动文档
│   ├── GetAllOpenDocumentsTool.vb # 获取所有打开文档
│   ├── RunProjectCustomToolsTool.vb # 运行自定义工具
│   ├── ReadLinesTool.vb          # 读取行
│   ├── AppendLinesTool.vb        # 追加行
│   ├── ReplaceLinesTool.vb       # 替换行
│   ├── WriteLinesTool.vb         # 写入行
│   ├── StringReplaceTool.vb      # 字符串替换
│   └── FileOperationHelper.vb    # 文件操作辅助
├── Models/                       # 数据模型
│   ├── McpModels.vb              # MCP 模型
│   ├── VsRpcModels.vb            # VS RPC 模型
│   ├── FileOperationModels.vb    # 文件操作模型
│   ├── PermissionModels.vb       # 权限模型
│   ├── PathPermissionPolicy.vb   # 路径权限策略
│   ├── FileAccessType.vb         # 文件访问类型枚举
│   └── PolicyCheckResult.vb      # 策略检查结果（新增）
├── Helpers/                      # 辅助类
│   ├── PathHelper.vb             # 路径辅助模块
│   ├── PathPolicyManager.vb      # 路径权限策略管理器（新增）
│   └── Abstractions.vb           # 抽象类定义
├── WpfDebug/                     # WPF 调试连接层（#22 新增）
│   ├── WpfDebugProxy.vb          # named pipe client（被控端代理；支持指定 PID / 枚举 / 直连 pipe 名）
│   ├── WpfDebugResultReader.vb   # OkResult 嵌套解析纯函数 + ParseHandshake（含 processPath）
│   ├── WpfDebugConnection.vb     # 连接握手快照（pid/title/version/processPath）
│   └── WpfDebugTargetEnumerator.vb # 被控端发现（枚举系统 pipe 按前缀解析 PID，不连 pipe）
├── My Project/                   # VB 项目资源
│   ├── Resources.Designer.vb     # 资源管理器
│   ├── Resources.resx            # 资源文件（默认）
│   └── Resources.zh-CN.resx      # 资源文件（中文）
└── McpServiceNetFx.Core.vbproj   # 项目文件
```

**MCP 协议组件**:
- JSON-RPC 2.0 消息处理
- HTTP 端点实现
- 工具调用路由
- 权限验证中间件
- 异常处理和错误响应

**WPF 调试连接层（主控侧，命名空间 `McpServiceNetFx`）**:

`WpfDebug/` 文件夹下是主控连接被控 WPF 进程的 pipe client + 解析辅助（#22/#23）:
- `WpfDebugProxy.vb` — named pipe client。pipe 名带 PID：支持 `New(targetPid)` 指定 PID、`New(pipeName)` 直连、`New()` 无参（ConnectAsync 时枚举系统 pipe 连第一个候选，向后兼容）。握手含 processPath 字段。
- `WpfDebugResultReader.vb` — OkResult 嵌套解析纯函数（`GetPayload` 取 `response.Result("result")`）+ `ParseHandshake`（解析 pid/title/version/processPath）
- `WpfDebugConnection.vb` — 连接握手快照（pid/title/version/processPath 四字段）
- `WpfDebugTargetEnumerator.vb` — 被控端发现：`DiscoverCandidates()` 枚举 `\\.\pipe\` 按 `WpfDebugProtocol.PipeNamePrefix` 前缀过滤解析 PID，用 `Process.GetProcessById` 查 title/path，**不连 pipe**（不占被控端连接位）。POCO `WpfDebugTargetInfo`（Pid/MainWindowTitle/ProcessPath）

主控侧的六个 WPF 调试工具（list_windows/take_snapshot/click/fill/evaluate/take_screenshot）实现在
`Tools/WpfDebug/` 文件夹（`WpfDebugToolBase` 中间基类 + 六个工具），注册在 `VisualStudioToolManager`，
连接被控端后由工具管理器 DC 注入 proxy。详见 features.md 第 17/18 条。

### 3. McpServiceNetFx.Tests - 单元测试项目

**技术栈**: VB.NET + .NET 10.0 + MSTest 4.0.1

**主要职责**:
- 为 Core 项目提供单元测试
- 测试 MCP 工具和辅助类
- 验证权限控制逻辑
- 测试路径处理和通配符匹配

**核心文件夹结构**:

```
McpServiceNetFx.Tests/
├── PathPermissionPolicyTests.vb  # PathPermissionPolicy 测试（原 Test1.vb）
├── PolicyCheckResultTests.vb     # PolicyCheckResult 测试（新增）
├── PathPolicyManagerTests.vb     # PathPolicyManager 测试（新增）
└── McpServiceNetFx.Tests.vbproj  # 项目文件
```

**测试覆盖范围**:
- PathHelper 路径标准化和通配符匹配测试
- 文件操作工具测试
- 权限模型测试
- MCP 协议组件测试

### 4. McpServiceNetFx.VsixAsync - Visual Studio 插件

**技术栈**: VB.NET + .NET Framework + VS SDK + Avalonia UI

**主要职责**:
- 提供 Visual Studio 集成
- 在 VS 中显示工具窗口
- 嵌入式 MCP 服务管理
- 与 VS IDE 深度集成
- 支持深色/浅色主题

**核心文件夹结构**:

```
McpServiceNetFx.VsixAsync/
├── McpServiceNetFx.VsixAsyncPackage.vb  # VS 包定义
├── Commands/                    # VS 命令
│   └── ShowMcpToolWindowCommand.vb # 显示工具窗口命令
├── ToolWindows/                 # 工具窗口
│   ├── McpToolWindow.vb         # 工具窗口定义
│   ├── McpToolWindowControl.xaml  # Avalonia XAML
│   ├── McpToolWindowControl.xaml.vb # 代码后置
│   └── McpWindowState.vb        # 窗口状态
├── Views/                       # 辅助视图
│   ├── CustomMessageBox.xaml    # 自定义消息框
│   └── UserMessageWindow.xaml   # 用户消息窗口
├── Helpers/                     # 辅助类
│   └── SettingsPersistenceHelper.vb # 设置持久化
├── VSCommandTable.vsct          # VS 命令表
├── source.extension.vsixmanifest # VSIX 清单
└── McpServiceNetFx.VsixAsync.vbproj # 项目文件
```

**VS 集成功能**:
- View -> Other Windows -> MCP Service Manager
- 与当前 VS 实例的直接集成
- 自动跟随 VS 主题
- 不支持控制其他 VS 实例

> 注：McpServiceNetFx.VsixAsync 已从 `McpServerForDevEnv.sln` 移出（独立维护），代码仍保留在仓库中。

---

## WPF 调试子系统（WpfDebugging）

#19–#24 新增的独立子系统，与主控（McpServiceNetFx.Core）解耦，靠 named pipe IPC 通信。
「主控 ↔ 被控端」清晰分离：主控在 McpServiceNetFx 进程内，被控端是任意宿主 SampleHost/第三方 WPF 程序，
二者通过固定 pipe 名（`mcpserverfordevenv.wpfdebug.v1`）握手，不共享进程状态。

子系统由「共享契约库 + 被控端实现 + 主控连接层 + 示例被控端 + 测试」五部分组成：

> 命名说明：以下 6 个项目的**文件夹名 / 项目文件名（.vbproj）已去掉 `McpServerForDevEnv.` 前缀**，统一为 `WpfDebugging.*`；
> 但 **AssemblyName / RootNamespace / 代码命名空间保持 `McpServerForDevEnv.WpfDebugging.*` 不变**（dll/exe 名与代码零改动）。

### 5. WpfDebugging.Core — IPC 共享契约库

**技术栈**: VB.NET + 多目标 `net472;net8.0-windows` + Newtonsoft.Json

**主要职责**:
- 定义主控↔被控端共享的 IPC 协议契约（零 WPF / EnvDTE 强依赖）
- POCO 消息类型、协议常量、Method 名、消息分帧器
- 被控端能力接口 `IWpfDebugTarget`（方法签名文档化）

**核心文件夹结构**:

```
WpfDebugging.Core/
├── Protocol/
│   ├── WpfDebugProtocol.vb        # 同文件含常量类 WpfDebugProtocol（PipeNamePrefix/PipeName/ProtocolVersion + GetPipeNameForPid）
│   │                              #   与 POCO 类 WpfDebugRequest/Response/Event/Error
│   ├── WpfDebugMethods.vb         # Method 名常量（list_windows/take_snapshot/...）
│   └── MessageFramer.vb           # 长度前缀分帧器（读写 JSON 消息）
├── IWpfDebugTarget.vb             # 被控端能力接口（13 个方法签名）
└── WpfDebugging.Core.vbproj
```

**关键约定**:
- pipe 名固定 `mcpserverfordevenv.wpfdebug.v1`（带协议主版本号，单被控语义，同名撞即报错）
- uid = `DependencyObject.GetHashCode()` 字符串（进程内稳定、跨快照不变、对象 GC 后失效）
- 多目标兼容 net472（AppDomain）与 net8.0+（AssemblyLoadContext）

### 6. WpfDebugging.Target — 被控端实现

**技术栈**: VB.NET + 多目标 `net472;net8.0-windows` + WPF + Roslyn (Microsoft.CodeAnalysis.VisualBasic)

**主要职责**:
- 宿主 WPF 进程内运行的被控端，实现 IWpfDebugTarget 六能力 MVP
- 启动 named pipe server，处理主控请求，调度到 UI 线程执行
- VB.NET 脚本引擎（evaluate）：Roslyn 编译 → 内存 PE → AssemblyLoader 加载 → 执行

**核心文件夹结构**:

```
WpfDebugging.Target/
├── WpfDebugHost.vb                # 被控端宿主入口（Start/Stop 生命周期）
├── WpfDebugHostConfig.vb          # 启动配置（EnableScripting 等）
├── WpfDebugTargetImpl.vb          # IWpfDebugTarget 实现
├── Pipe/
│   └── WpfDebugPipeServer.vb      # named pipe server（监听 + 分派 + OkResult 嵌套封装）
├── Handshake/                     # 握手帧（pid/主窗口标题/协议版本）
├── Dispatcher/                    # WpfDispatcher（UI 线程调度）
├── Ui/
│   └── UidRegistry.vb             # uid ↔ DependencyObject 映射注册表
├── VisualTree/                    # 可视树快照（SnapshotNode + InterestingNodeFilter）
├── Windows/                       # 窗口枚举 + WindowCapturer（截图）
├── Interaction/                   # ElementInteractor（click/fill 按控件类型分流）
├── Scripting/
│   ├── ScriptEngine.vb            # Roslyn 脚本编译执行（懒加载）
│   ├── AssemblyLoader.vb          # 双目标程序集加载器（#21 M1 修复：临时 ALC 挂 Resolving）
│   ├── ScriptHost.vb              # 脚本宿主 API（暴露 uid/窗口/日志）
│   └── ScriptResultSerializer.vb  # 脚本返回值序列化
└── Capture/                       # 截图支持
```

### 7. WpfDebugging.SampleHost — 示例被控端宿主

**技术栈**: VB.NET + 多目标 `net472;net8.0-windows` + WPF

**主要职责**（#24 端到端联调用）:
- 启动时调 `WpfDebugHost.Start()` 起 PID 名 pipe server（`GetPipeNameForPid(自身pid)`）
- 主窗口放一批测试控件（TextBox/Button/CheckBox/ComboBox/Slider/ListBox/Label 等）供联调
- 关键控件给 x:Name，方便 list_windows/take_snapshot 定位、click/fill 找目标、evaluate 脚本操作
- 退出时 `WpfDebugHost.Stop()` 清理

**核心文件结构**:

```
WpfDebugging.SampleHost/
├── Application.xaml(.vb)           # OnStartup 启 host / OnExit 停 host
├── MainWindow.xaml(.vb)            # 测试控件窗（x:Name 命名 + 交互回显）
└── WpfDebugging.SampleHost.vbproj
```

### 8. WPF 调试测试项目（三个）

| 项目 | 语言/框架 | 被测对象 | 是否进默认 `dotnet test` |
|------|----------|---------|------------------------|
| WpfDebugging.Core.Tests | VB.NET + MSTest 4.0.1 + net10.0-windows | WpfDebugging.Core（MessageFramer/序列化/SnapshotNode 纯逻辑） | 是（无副作用） |
| WpfDebugging.Target.Tests | **C#** + MSTest 3.6.4 + net10.0-windows | WpfDebugging.Target（ElementInteractor/SnapshotFormatter/UidRegistry/WindowCapturer/ScriptResultSerializer） | 是（无副作用） |
| WpfDebugging.IntegrationTests | VB.NET + MSTest 4.0.1 + net472 | 主控↔被控端 pipe 链路（启 SampleHost 进程 + WpfDebugProxy 调六 Method） | **否**（有副作用，独立项目不进 sln，`TestCategory("Integration")`） |

> Target.Tests 用 C# 的原因：本环境 VB 编译器对「WPF 全 ref + 测试 SDK」组合异常退出（MSB6006），C# 的 csc 稳定。
> IntegrationTests 用 VB.NET（不直接碰 WPF 控件，只调主控 pipe client），且独立于 sln，默认 `dotnet test McpServerForDevEnv.sln` 完全不扫到它。

## 项目依赖关系

```
主控侧（VS MCP 服务）
─────────────────────
McpServiceNetFx.VsixAsync ──→ McpServiceNetFx.Core
McpServiceNetFx            ──→ McpServiceNetFx.Core
                                    │
                                    ├──→ WpfDebugging.Core（IPC 契约，复用 pipe 协议）

WPF 调试子系统（被控端 + 示例 + 测试）
─────────────────────────────────────
WpfDebugging.Target      ──→ WpfDebugging.Core（契约）
WpfDebugging.SampleHost  ──→ WpfDebugging.Target（宿主 WpfDebugHost）

WpfDebugging.Core.Tests         ──→ WpfDebugging.Core
WpfDebugging.Target.Tests (C#)  ──→ WpfDebugging.Target
WpfDebugging.IntegrationTests   ──→ McpServiceNetFx.Core（主控 WpfDebugProxy），运行时启动 SampleHost exe
```

**依赖说明**:
- 主控侧 McpServiceNetFx.Core 复用 WpfDebugging.Core 的 IPC 契约（Request/Response/Method 常量/MessageFramer），不改契约
- 主控 pipe client（WpfDebugProxy / WpfDebugResultReader）在 McpServiceNetFx.Core 的 `WpfDebug/` 文件夹下，命名空间 `McpServiceNetFx`
- 被控端 WpfDebugging.Target 是独立实现，主控不引用它（运行时靠 pipe 协议解耦）
- SampleHost 引用 Target 的 WpfDebugHost 起被控端
- IntegrationTests 只引主控 McpServiceNetFx.Core，启动 SampleHost 进程做端到端验证
- Core 项目不依赖任何 UI 项目，可独立测试

## 技术栈总结

| 层次 | 项目 | 技术栈 | 主要框架 |
|------|------|--------|----------|
| 表现层 | McpServiceNetFx | VB.NET + .NET Framework | WPF + WPF-UI |
| 测试层 | McpServiceNetFx.Tests | VB.NET + .NET Framework (net472) | MSTest 4.0.1 |
| 表现层 | McpServiceNetFx.VsixAsync | VB.NET + .NET Framework | Avalonia UI + VS SDK（已出 sln） |
| 核心层 | McpServiceNetFx.Core | VB.NET + .NET Framework (net472) | System.Net.Http + WPF 调试主控连接层 |
| IPC 契约 | WpfDebugging.Core | VB.NET + 多目标 net472;net8.0-windows | Newtonsoft.Json |
| 被控端 | WpfDebugging.Target | VB.NET + 多目标 net472;net8.0-windows | WPF + Roslyn (CodeAnalysis.VisualBasic) |
| 示例被控端 | WpfDebugging.SampleHost | VB.NET + 多目标 net472;net8.0-windows | WPF |
| Wpf 调试测试 | WpfDebugging.Core.Tests | VB.NET + net10.0-windows | MSTest 4.0.1 |
| Wpf 调试测试 | WpfDebugging.Target.Tests | C# + net10.0-windows | MSTest 3.6.4 + Test.Sdk 17.12 |
| Wpf 调试测试 | WpfDebugging.IntegrationTests | VB.NET + net472 | MSTest 4.0.1（不进 sln，TestCategory=Integration） |

## 架构优势

1. **关注点分离**: UI、业务逻辑、VS 集成各司其职
2. **代码复用**: Core 项目被两个 UI 项目共享
3. **独立部署**: 独立应用和 VS 插件可以独立发布
4. **易于测试**: Core 项目可以独立进行单元测试
5. **灵活扩展**: 可以添加新的 UI 项目而不影响 Core

## 常见架构问题

### Q: 为什么 Core 项目不依赖 UI 项目？

A: 遵循依赖倒置原则，核心业务逻辑不应该依赖于表现层。这样 Core 可以被多个 UI 项目共享，也可以独立测试。

### Q: 为什么有两个 UI 项目？

A: 满足不同使用场景：
- **McpServiceNetFx**: 独立应用，可以管理多个 VS 实例
- **McpServiceNetFx.VsixAsync**: VS 插件，与 VS 深度集成

### Q: 如何添加新的 MCP 工具？

A:
1. 在 `McpServiceNetFx.Core/Tools/` 创建新的工具类
2. 继承 `VisualStudioToolBase`
3. 在 `VisualStudioTools.vb` 中注册工具
4. 在 UI 中添加对应的权限配置项

### Q: VS RPC 通信是如何实现的？

A: 通过 `VsRpcModels.vb` 中定义的数据模型，Core 项目通过 RPC 与 VS 实例通信。通信细节在 `VisualStudioToolBase.vb` 和 `VisualStudioToolManager.vb` 中实现。
