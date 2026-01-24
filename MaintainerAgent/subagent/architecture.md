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
│   └── VisualStudioMonitor.vb    # VS 实例监控
├── Views/                        # XAML 视图
│   ├── MainWindow.xaml           # 主窗口
│   ├── MainWindow.xaml.vb        # 主窗口代码后置
│   ├── MainWindow.Logging.vb     # 日志功能
│   ├── MainWindow.McpService.vb  # MCP 服务管理
│   ├── MainWindow.Permissions.vb # 权限配置
│   ├── MainWindow.VsInstances.vb # VS 实例管理
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
│   └── PermissionModels.vb       # 权限模型
├── Helpers/                      # 辅助类
│   └── Abstractions.vb           # 抽象类定义
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
├── Test1.vb                     # 测试类
└── McpServiceNetFx.Tests.vbproj # 项目文件
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

## 项目依赖关系

```
McpServiceNetFx.VsixAsync
    │
    ├── references ──→ McpServiceNetFx.Core
    │                      (MCP 协议实现)
    │
McpServiceNetFx
    │
    └── references ──→ McpServiceNetFx.Core
                           (MCP 协议实现)
```

**依赖说明**:
- 两个 UI 项目都引用 Core 项目
- Core 项目不依赖任何 UI 项目
- Core 项目可以被独立使用和测试
- UI 项目之间的依赖关系：无（独立部署）

## 技术栈总结

| 层次 | 项目 | 技术栈 | 主要框架 |
|------|------|--------|----------|
| 表现层 | McpServiceNetFx | VB.NET + .NET Framework | WPF + WPF-UI |
| 测试层 | McpServiceNetFx.Tests | VB.NET + .NET 10.0 | MSTest |
| 表现层 | McpServiceNetFx.VsixAsync | VB.NET + .NET Framework | Avalonia UI + VS SDK |
| 核心层 | McpServiceNetFx.Core | VB.NET + .NET Framework | System.Net.Http |

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
