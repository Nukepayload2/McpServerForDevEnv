# McpServerForDevEnv 项目智能体协作指南

本项目配备维护者智能体，用于缓存项目知识和协助开发工作。

## 可用智能体

| 智能体 | 位置 | 功能描述 |
|--------|------|----------|
| **McpServerMaintainer** | `./MaintainerAgent/main.md` | 项目维护智能体，提供项目架构、功能和源码知识缓存 |
| **VbNetDev** | `G:\Projects\CodeAtelier\MagicTools\VbNetDev\main.md` | VB.NET 代码开发 |
| **VbNetReviewer** | `G:\Projects\CodeAtelier\MagicTools\VbNetReviewer\main.md` | VB.NET 代码审查 |

## 智能体协作关系

```
                 ┌─────────────────────────────────┐
                 │   McpServerMaintainer (本机)      │
                 │   - 项目架构知识缓存              │
                 │   - 功能源码定位                  │
                 │   - 代码上下文提供                │
                 └─────────────────┬───────────────┘
                                   │
                                   │ 按需调用
                                   │
                    ┌──────────────┴──────────────┐
                    │                             │
                    ▼                             ▼
        ┌───────────────────────┐   ┌───────────────────────┐
        │     VbNetDev          │   │   VbNetReviewer       │
        │   - 代码开发           │   │   - 代码审查           │
        │   - 遵循 VB 规范       │   │   - 编码规范检查       │
        │   - 编译验证           │   │   - 架构设计审查       │
        └───────────────────────┘   └───────────────────────┘
```

## 使用场景

### 1. 项目架构查询
直接读取 `./MaintainerAgent/subagent/architecture.md` 了解项目结构，无需扫描代码。

### 2. 功能源码定位
直接读取 `./MaintainerAgent/subagent/features.md` 获取功能实现范围，无需搜索。

### 3. 代码开发任务
调用 VbNetDev 执行代码开发，Maintainer 提供源码定位支持。

### 4. 代码审查任务
调用 VbNetReviewer 执行代码审查，Maintainer 提供架构上下文。

## 按需调用原则

- **简单问题**：直接读取 MaintainerAgent 的子智能体文件（architecture.md, features.md）
- **开发任务**：使用 Task 工具调用 `VbNetDev`
- **审查任务**：使用 Task 工具调用 `VbNetReviewer`
- **复杂任务**：先读取 MaintainerAgent 获取上下文，再调用相应智能体执行

## 重要规则

### 架构和功能更改时更新 MaintainerAgent

当进行以下更改时，**必须同步更新** MaintainerAgent 的知识缓存：

| 更改类型 | 更新文件 |
|---------|---------|
| 新增/删除项目 | `MaintainerAgent/subagent/architecture.md` |
| 新增/删除文件夹 | `MaintainerAgent/subagent/architecture.md` |
| 新增 MCP 工具 | `MaintainerAgent/subagent/features.md` |
| 修改功能实现位置 | `MaintainerAgent/subagent/features.md` |
| 新增权限配置项 | `MaintainerAgent/subagent/features.md` |

**更新流程**:
1. 完成代码更改后
2. 编辑对应的 `.md` 文件
3. 保持与实际代码结构一致

## 调用示例

### 开发新 MCP 工具
```markdown
1. 读取 MaintainerAgent/subagent/features.md 了解现有工具结构
2. 调用 VbNetDev 执行开发，提供源码位置参考
```

### 修改 UI 界面
```markdown
1. 读取 MaintainerAgent/subagent/architecture.md 了解 UI 项目结构
2. 调用 VbNetDev 执行开发，提供 XAML/VB 文件位置
```

### 代码审查
```markdown
1. 调用 VbNetReviewer 执行全面审查
2. MaintainerAgent 自动提供架构上下文
```

## 项目技术栈

- **语言**: VB.NET
- **框架**: .NET Framework 4.7.2+
- **UI**: WPF (独立应用) + WPF-UI (VSIX 插件)
- **协议**: MCP (Model Context Protocol) over HTTP
- **集成**: Visual Studio 2015+ (通过 DTE/EnvDTE)
