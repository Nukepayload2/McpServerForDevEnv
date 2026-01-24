---
name: Features
description: 功能维护子智能体，描述 McpServerForDevEnv 的核心功能、源码范围和工具定义
input:
  - query: 功能相关问题
output:
  - 功能描述、源码范围、工具定位信息
---

# 功能维护子智能体

## 职责

维护 McpServerForDevEnv 项目的功能知识，包括核心功能列表、每个功能的源码范围、工具定义和实现位置。

## 核心功能分类

### 1. 构建功能 (Build Features)

#### 1.1 构建解决方案 (Build Solution)

**功能描述**: 构建整个解决方案，支持 Debug/Release 配置

**源码范围**:
```
定义: McpServiceNetFx.Core/Tools/BuildSolutionTool.vb
注册: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
模型: McpServiceNetFx.Core/Models/VsRpcModels.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
```

**MCP 工具名**: `build_solution`

**参数**:
- `configuration`: Debug 或 Release

**权限配置**: UI 中的 "Build Solution" 权限项

#### 1.2 构建项目 (Build Project)

**功能描述**: 构建指定项目，支持 Debug/Release 配置

**源码范围**:
```
定义: McpServiceNetFx.Core/Tools/BuildProjectTool.vb
注册: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
模型: McpServiceNetFx.Core/Models/VsRpcModels.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
```

**MCP 工具名**: `build_project`

**参数**:
- `projectPath`: 项目路径
- `configuration`: Debug 或 Release

**权限配置**: UI 中的 "Build Project" 权限项

### 2. 错误管理 (Error Management)

#### 2.1 获取错误列表 (Get Error List)

**功能描述**: 获取当前解决方案中的错误列表

**源码范围**:
```
定义: McpServiceNetFx.Core/Tools/GetErrorListTool.vb
注册: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
模型: McpServiceNetFx.Core/Models/VsRpcModels.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
```

**MCP 工具名**: `get_error_list`

**参数**: 无

**返回数据**:
- 错误文件路径
- 错误行号
- 错误描述
- 错误级别（Error/Warning）

**权限配置**: UI 中的 "Get Error List" 权限项

### 3. 文档管理 (Document Management)

#### 3.1 获取活动文档 (Get Active Document)

**功能描述**: 获取当前在 VS 中打开的活动文档信息

**源码范围**:
```
定义: McpServiceNetFx.Core/Tools/GetActiveDocumentTool.vb
注册: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
模型: McpServiceNetFx.Core/Models/VsRpcModels.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
```

**MCP 工具名**: `get_active_document`

**参数**: 无

**返回数据**:
- 文档路径
- 文件名
- 当前选择/光标位置

**权限配置**: UI 中的 "Get Active Document" 权限项

#### 3.2 获取所有打开文档 (Get All Open Documents)

**功能描述**: 获取 VS 中所有打开的文档列表

**源码范围**:
```
定义: McpServiceNetFx.Core/Tools/GetAllOpenDocumentsTool.vb
注册: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
模型: McpServiceNetFx.Core/Models/VsRpcModels.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
```

**MCP 工具名**: `get_all_open_documents`

**参数**: 无

**返回数据**: 文档路径数组

**权限配置**: UI 中的 "Get All Open Documents" 权限项

### 4. 文件操作 (File Operations)

#### 4.1 读取行 (Read Lines)

**功能描述**: 读取文件的指定行范围，支持路径策略自动授权

**源码范围**:
```
定义: McpServiceNetFx.Core/Tools/ReadLinesTool.vb
注册: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
模型: McpServiceNetFx.Core/Models/FileOperationModels.vb
辅助: McpServiceNetFx.Core/Tools/FileOperationHelper.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
权限: McpServiceNetFx.Core/Models/FileAccessType.vb
```

**MCP 工具名**: `read_lines`

**参数**:
- `filePath`: 文件路径
- `startLine`: 起始行号（1-based）
- `endLine`: 结束行号（可选，默认读取到文件末尾）

**权限配置**: UI 中的 "Read Lines" 权限项，支持 AlwaysAsk 级别
**访问类型**: Read

#### 4.2 写入行 (Write Lines)

**功能描述**: 向文件写入内容，覆盖原内容，支持路径策略自动授权

**源码范围**:
```
定义: McpServiceNetFx.Core/Tools/WriteLinesTool.vb
注册: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
模型: McpServiceNetFx.Core/Models/FileOperationModels.vb
辅助: McpServiceNetFx.Core/Tools/FileOperationHelper.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
权限: McpServiceNetFx.Core/Models/FileAccessType.vb
```

**MCP 工具名**: `write_lines`

**参数**:
- `filePath`: 文件路径
- `lines`: 行内容数组
- `startLine`: 起始行号（可选，默认从第 1 行开始）

**权限配置**: UI 中的 "Write Lines" 权限项，支持 AlwaysAsk 级别
**访问类型**: Write

#### 4.3 追加行 (Append Lines)

**功能描述**: 在文件末尾追加内容，支持路径策略自动授权

**源码范围**:
```
定义: McpServiceNetFx.Core/Tools/AppendLinesTool.vb
注册: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
模型: McpServiceNetFx.Core/Models/FileOperationModels.vb
辅助: McpServiceNetFx.Core/Tools/FileOperationHelper.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
权限: McpServiceNetFx.Core/Models/FileAccessType.vb
```

**MCP 工具名**: `append_lines`

**参数**:
- `filePath`: 文件路径
- `lines`: 行内容数组

**权限配置**: UI 中的 "Append Lines" 权限项，支持 AlwaysAsk 级别
**访问类型**: Write

#### 4.4 替换行 (Replace Lines)

**功能描述**: 替换文件中的指定行范围，支持路径策略自动授权

**源码范围**:
```
定义: McpServiceNetFx.Core/Tools/ReplaceLinesTool.vb
注册: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
模型: McpServiceNetFx.Core/Models/FileOperationModels.vb
辅助: McpServiceNetFx.Core/Tools/FileOperationHelper.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
权限: McpServiceNetFx.Core/Models/FileAccessType.vb
```

**MCP 工具名**: `replace_lines`

**参数**:
- `filePath`: 文件路径
- `startLine`: 起始行号
- `endLine`: 结束行号
- `lines`: 替换内容数组

**权限配置**: UI 中的 "Replace Lines" 权限项，支持 AlwaysAsk 级别
**访问类型**: ReadWrite

#### 4.5 字符串替换 (String Replace)

**功能描述**: 在文件中替换字符串，支持路径策略自动授权

**源码范围**:
```
定义: McpServiceNetFx.Core/Tools/StringReplaceTool.vb
注册: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
模型: McpServiceNetFx.Core/Models/FileOperationModels.vb
辅助: McpServiceNetFx.Core/Tools/FileOperationHelper.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
权限: McpServiceNetFx.Core/Models/FileAccessType.vb
```

**MCP 工具名**: `string_replace`

**参数**:
- `filePath`: 文件路径
- `oldString`: 要替换的字符串
- `newString`: 替换后的字符串
- `replaceAll`: 是否替换所有出现（可选，默认 false）

**权限配置**: UI 中的 "String Replace" 权限项，支持 AlwaysAsk 级别
**访问类型**: ReadWrite

### 5. 解决方案信息 (Solution Information)

#### 5.1 获取解决方案信息 (Get Solution Info)

**功能描述**: 获取当前解决方案的信息和结构

**源码范围**:
```
定义: McpServiceNetFx.Core/Tools/GetSolutionInfoTool.vb
注册: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
模型: McpServiceNetFx.Core/Models/VsRpcModels.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
```

**MCP 工具名**: `get_solution_info`

**参数**: 无

**返回数据**:
- 解决方案路径
- 解决方案名称
- 项目列表

**权限配置**: UI 中的 "Get Solution Info" 权限项

### 6. 自定义工具 (Custom Tools)

#### 6.1 运行项目自定义工具 (Run Project Custom Tools)

**功能描述**: 执行指定项目的自定义工具（如运行自定义工具、生成代码等）

**源码范围**:
```
定义: McpServiceNetFx.Core/Tools/RunProjectCustomToolsTool.vb
注册: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
模型: McpServiceNetFx.Core/Models/VsRpcModels.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
```

**MCP 工具名**: `run_project_custom_tools`

**参数**:
- `projectPath`: 项目路径

**权限配置**: UI 中的 "Run Custom Tools" 权限项

## Visual Studio 集成功能

### 7. VS 实例管理 (VS Instance Management)

#### 7.1 VS 实例枚举

**功能描述**: 自动检测系统中的多个 Visual Studio 实例

**源码范围**:
```
实现: McpServiceNetFx/Helpers/VisualStudioEnumerator.vb
UI: McpServiceNetFx/Views/MainWindow.VsInstances.vb
```

**功能**:
- 检测 VS 2015 到最新版本
- 显示 VS 版本和实例 ID
- 支持多实例同时运行

#### 7.2 VS 实例监控

**功能描述**: 监控 VS 实例的运行状态

**源码范围**:
```
实现: McpServiceNetFx/Helpers/VisualStudioMonitor.vb
UI: McpServiceNetFx/Views/MainWindow.VsInstances.vb
```

**功能**:
- 实时监控 VS 进程状态
- VS 退出时自动断开连接
- 状态变化时更新 UI

### 8. VS 工具窗口 (VS Tool Window)

**功能描述**: 在 Visual Studio 中显示 MCP 服务管理器

**源码范围**:
```
定义: McpServiceNetFx.VsixAsync/ToolWindows/McpToolWindow.vb
视图: McpServiceNetFx.VsixAsync/ToolWindows/McpToolWindowControl.xaml
命令: McpServiceNetFx.VsixAsync/Commands/ShowMcpToolWindowCommand.vb
清单: McpServiceNetFx.VsixAsync/VSCommandTable.vsct
```

**入口**: View -> Other Windows -> MCP Service Manager

**功能**:
- 与独立应用相同的 MCP 功能
- 跟随 VS 主题（深色/浅色）
- 只管理当前 VS 实例

## 权限控制 (Permission Control)

### 9. 权限管理系统

**功能描述**: 为每个 MCP 功能配置独立的权限级别，支持路径通配符自动应答策略

**源码范围**:
```
接口: McpServiceNetFx.Core/Mcp/IMcpPermissionHandler.vb
模型: McpServiceNetFx.Core/Models/PermissionModels.vb
模型: McpServiceNetFx.Core/Models/FileAccessType.vb
模型: McpServiceNetFx.Core/Models/PathPermissionPolicy.vb
辅助: McpServiceNetFx.Core/Helpers/PathHelper.vb
UI: McpServiceNetFx/Views/MainWindow.Permissions.vb
UI: McpServiceNetFx/Views/PermissionConfirmDialog.xaml
转换器: McpServiceNetFx/Converters/PermissionLevelConverter.vb
转换器: McpServiceNetFx/Converters/FileAccessTypeConverter.vb
持久化: McpServiceNetFx/Helpers/PersistenceModule.vb
```

**权限级别**:
- **Allow**: 自动允许操作
- **Ask**: 每次操作前询问用户，支持路径策略自动应答
- **AlwaysAsk**: 总是询问（仅文件工具可用），跳过路径策略
- **Deny**: 拒绝操作

**文件访问类型**:
- **Read**: 读取访问
- **Write**: 写入访问
- **ReadWrite**: 读写访问

**路径通配符策略**:
- 支持允许列表和拒绝列表
- 拒绝列表优先级高于允许列表
- 支持通配符模式匹配（使用 VB Like 运算符）
- 支持路径标准化（POSIX 路径、用户目录 ~）
- 未匹配路径仍会弹出确认对话框（Ask 模式下）

**权限检查流程**:
```
CheckFilePermission(filePath, accessType)
│
└─ 功能权限检查（顶级）
    ├─ Allow → 允许
    ├─ Deny → 拒绝
    ├─ Ask → 进入路径策略检查
    │   ├─ 拒绝列表 → 匹配 → 拒绝
    │   ├─ 允许列表 → 匹配 → 允许
    │   └─ 未匹配 → 弹增强对话框（可配置策略）
    └─ AlwaysAsk → 弹基础对话框（无策略配置）
```

**增强权限确认对话框**:
- 显示功能名称、操作描述、文件路径
- 可展开的"默认应答"区域
- 支持快速添加允许/拒绝策略
- 可编辑的通配符模式输入框

**UI 配置位置**: 主窗口的 MCP Permissions 标签页
- 功能权限表格：配置每个工具的权限级别
- Ask 模式自动授权策略：配置路径通配符策略

## MCP 服务 (MCP Service)

### 10. HTTP 服务

**功能描述**: 提供 HTTP 端点供 MCP 客户端连接

**源码范围**:
```
实现: McpServiceNetFx.Core/Mcp/VisualStudioMcpHttpService.vb
服务: McpServiceNetFx.Core/Mcp/McpService.vb
日志: McpServiceNetFx.Core/Mcp/IMcpLogger.vb
```

**协议**: 基于 HTTP 的 JSON-RPC 2.0

**端口配置**: UI 中可配置服务端口

**绑定**: 仅绑定到 localhost (127.0.0.1)，防止外部访问

### 11. MCP 工具注册

**功能描述**: 注册和管理所有 MCP 工具

**源码范围**:
```
实现: McpServiceNetFx.Core/Mcp/VisualStudioMcpTools.vb
基类: McpServiceNetFx.Core/Tools/VisualStudioToolBase.vb
管理: McpServiceNetFx.Core/Tools/VisualStudioToolManager.vb
```

**工具注册方式**:
- 每个工具继承 `VisualStudioToolBase`
- 在 `VisualStudioTools.vb` 中注册
- 工具管理器负责调用和权限验证

## UI 功能

### 12. 主窗口功能

**源码范围**:
```
主窗口: McpServiceNetFx/Views/MainWindow.xaml
代码: McpServiceNetFx/Views/MainWindow.xaml.vb
模块化功能:
  - McpServiceNetFx/Views/MainWindow.Logging.vb
  - McpServiceNetFx/Views/MainWindow.McpService.vb
  - McpServiceNetFx/Views/MainWindow.Permissions.vb
  - McpServiceNetFx/Views/MainWindow.VsInstances.vb
```

**标签页**:
1. **VS Instances**: 选择和管理 VS 实例
2. **MCP Service**: 启动/停止服务，查看连接状态
3. **Permissions**: 配置每个功能的权限级别
4. **Logging**: 查看实时日志

### 13. 自定义消息框

**源码范围**:
```
独立应用: McpServiceNetFx/Views/CustomMessageBox.xaml
VS 插件: McpServiceNetFx.VsixAsync/Views/CustomMessageBox.xaml
```

**功能**: 替代标准 MsgBox，提供更好的 UI 体验

### 14. 用户消息窗口

**源码范围**:
```
独立应用: McpServiceNetFx/Views/UserMessageWindow.xaml
VS 插件: McpServiceNetFx.VsixAsync/Views/UserMessageWindow.xaml
```

**功能**: 显示用户请求操作的消息（Ask 权限级别）

## 辅助功能

### 15. 设置持久化

**源码范围**:
```
独立应用: McpServiceNetFx/Helpers/PersistenceModule.vb
VS 插件: McpServiceNetFx.VsixAsync/Helpers/SettingsPersistenceHelper.vb
```

**保存内容**:
- 选中的 VS 实例
- 服务端口配置
- 权限配置
- 窗口状态

### 16. 日志系统

**源码范围**:
```
接口: McpServiceNetFx.Core/Mcp/IMcpLogger.vb
UI: McpServiceNetFx/Views/MainWindow.Logging.vb
```

**日志级别**:
- Debug
- Info
- Warning
- Error

## 工具开发指南

### 如何添加新的 MCP 工具

1. **创建工具类**
   - 在 `McpServiceNetFx.Core/Tools/` 创建新文件
   - 继承 `VisualStudioToolBase`
   - 实现 `ExecuteAsync` 方法

2. **定义参数模型**（如需要）
   - 在 `McpServiceNetFx.Core/Models/` 创建模型类

3. **注册工具**
   - 在 `VisualStudioTools.vb` 中添加工具定义
   - 指定工具名称、描述、参数

4. **添加权限配置**
   - 在 `MainWindow.Permissions.vb` 添加权限配置项
   - 在权限模型中添加对应字段

5. **测试工具**
   - 通过 MCP 客户端测试
   - 验证权限控制

### 工具基类功能

`VisualStudioToolBase` 提供:
- VS RPC 通信封装
- 权限验证
- 异常处理
- 日志记录

## 功能查询指南

### 查询构建相关功能
- 关键词: build, compile, solution, project
- 相关文件: BuildSolutionTool.vb, BuildProjectTool.vb

### 查询错误相关功能
- 关键词: error, warning, issue
- 相关文件: GetErrorListTool.vb

### 查询文档相关功能
- 关键词: document, file, editor, open
- 相关文件: GetActiveDocumentTool.vb, GetAllOpenDocumentsTool.vb

### 查询文件操作功能
- 关键词: read, write, append, replace, edit
- 相关文件: ReadLinesTool.vb, WriteLinesTool.vb, AppendLinesTool.vb, ReplaceLinesTool.vb, StringReplaceTool.vb

### 查询权限相关功能
- 关键词: permission, security, allow, deny, ask, always ask
- 相关文件: IMcpPermissionHandler.vb, PermissionModels.vb, FileAccessType.vb, PathPermissionPolicy.vb, PathHelper.vb, MainWindow.Permissions.vb, PermissionConfirmDialog.xaml

### 查询路径策略相关功能
- 关键词: path policy, wildcard, pattern, allow list, deny list
- 相关文件: PathPermissionPolicy.vb, PathHelper.vb, MainWindow.Permissions.vb, PersistenceModule.vb

### 查询 VS 集成相关功能
- 关键词: VS, Visual Studio, instance, monitor, toolwindow
- 相关文件: VisualStudioEnumerator.vb, VisualStudioMonitor.vb, McpToolWindow.vb
