# McpServerForDevEnv

Visual Studio MCP 服务器 GUI，为 AI 助手提供与 Visual Studio 集成的开发环境能力。

## 进度
项目处于 Beta 阶段

- [x] 外挂式服务管理器
- [ ] VSIX 内嵌式服务管理器

## 功能特性

在本地将选定的 Visual Studio 实例通过基于 HTTP 的 MCP 协议暴露给 AI 编程工具

### 核心功能
- **构建**：支持构建整个解决方案或指定项目，可选择 Debug/Release 配置
- **错误管理**：获取当前解决方案中的错误列表
- **文档管理**：获取当前活动文档信息和所有打开文档列表
- **解决方案信息**：获取当前解决方案的位置和结构

### Visual Studio 集成
- **自动列出**：自动检测多个 Visual Studio 实例
- **单个控制**：从列出的 Visual Studio 实例选择想要控制的，作为 MCP 服务公开
- **实时监控**：监控 Visual Studio 运行状态，退出后自动断开
- **版本兼容**：支持 Visual Studio 从 2015 到最新版

### 权限控制
- **细粒度权限**：为每个 MCP 功能配置独立的权限级别
- **三种权限模式**：允许、询问、拒绝
- **安全管控**：确保 AI 助手只能执行授权的操作

### 用户界面
- **现代化界面**：基于 WPF-UI 框架的 Windows 11 风格界面
- **多标签页设计**：服务管理、权限配置、日志查看分离管理
- **实时状态显示**：动态更新服务状态和连接信息

## 运行环境
- Windows 11: 无依赖
- 早期版本的 Windows: .NET Framework 版本 >= 4.7.2

## 项目结构

正式项目
```
McpServiceNetFx/
├── Helpers/           # 辅助工具类
├── Mcp/              # MCP 服务核心实现
├── Models/           # 数据模型
├── Tools/            # Visual Studio 工具集成
└── Views/            # WPF 界面组件
```

研究项目在 Research 里面

## 使用方式

1. 启动应用程序并选择目标 Visual Studio 实例
2. 配置服务端口并启动 MCP 服务器
3. 将生成的配置添加到 Claude Desktop 或其他 MCP 客户端
4. 根据需要配置各功能的权限级别
5. 通过日志监控服务运行状态

## 安全性

- 权限控制确保 AI 助手只能执行授权操作
- 本地服务仅在用户授权后与 Visual Studio 交互
- 详细的操作日志提供完整审计跟踪