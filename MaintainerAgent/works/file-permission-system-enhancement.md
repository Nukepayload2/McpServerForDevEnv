---
name: 文件 IO 工具授权系统增强
date: 2025-01-24
status: active
related-features:
  - 权限管理系统
  - 文件操作工具 (Read/Write/Append/Replace/StringReplace)
  - 权限确认对话框
---

# 文件 IO 工具授权系统增强

## 工作描述

为文件操作类 MCP 工具增强授权机制，引入路径通配符自动应答策略。

### 核心需求

1. **权限级别扩展**: 新增 `AlwaysAsk` 级别，绕过自动应答机制
2. **路径通配符策略**: 用户可配置允许/拒绝列表，支持通配符匹配
3. **路径标准化**: 支持 POSIX 路径、用户目录 (~) 转换
4. **访问类型区分**: 读/写/读写三种权限类型
5. **拒绝优先**: 拒绝列表优先级高于允许列表
6. **增强对话框**: Ask 对话框增加 Expander，可快速配置策略
7. **UI 差异化**: 只有文件操作工具可选择 AlwaysAsk

## 相关上下文

### 架构相关
- 涉及项目: McpServiceNetFx, McpServiceNetFx.Core, McpServiceNetFx.VsixAsync
- 相关模块: Models, Mcp, Tools, Views, Helpers

### 参考代码
- `G:\Projects\AITextBoxTest\AITextBoxTest\Helpers\PathHelper.vb` - 通配符匹配参考

## 工作记录

### 2025-01-24 - 设计方案确定

**状态**: pending

**权限级别定义**:

| 级别 | 本地化 | 行为 | 适用范围 |
|------|--------|------|----------|
| Allow | 自动允许 | 直接允许 | 所有工具 |
| Ask | 按需询问 | 检查路径策略，未匹配则弹框 | 所有工具 |
| AlwaysAsk | 总是询问 | 跳过路径策略，每次弹框 | 仅文件工具 |
| Deny | 自动拒绝 | 直接拒绝 | 所有工具 |

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

**新增/修改文件清单**:

| 序号 | 文件 | 操作 | 设计意图 |
|------|------|------|----------|
| 1 | `PermissionModels.vb` | 修改 | 枚举增加 AlwaysAsk，增加权限级别集合属性 |
| 2 | `FileAccessType.vb` | 新建 | 读/写/读写三种访问类型 |
| 3 | `PathPermissionPolicy.vb` | 新建 | 路径策略模型，包含策略类型+访问类型+通配符模式 |
| 4 | `PathHelper.vb` | 新建 | 合并路径标准化和通配符匹配功能 |
| 5 | `IMcpPermissionHandler.vb` | 修改 | 接口增加 CheckFilePermission 方法声明 |
| 6 | `PermissionConfirmDialog.xaml` | 新建 | 增强的权限确认对话框（含 Expander） |
| 7 | `PermissionConfirmDialog.xaml.vb` | 新建 | 对话框逻辑，处理策略配置结果 |
| 8 | `MainWindow.Permissions.vb` | 修改 | 实现路径策略检查逻辑，策略列表管理 |
| 9 | `VisualStudioToolBase.vb` | 修改 | 增加 IsFileTool 属性和 CheckFilePermission 方法 |
| 10-14 | 五个文件工具类 | 修改 | 标记为文件工具，调用新的权限检查方法 |
| 15 | `MainWindow.xaml` | 修改 | 添加路径策略配置 UI，使用 Converter 实现本地化 |
| 16 | `PermissionLevelConverter.vb` | 新建 | IValueConverter，从资源文件查找本地化字符串 |
| 17 | `PersistenceModule.vb` | 修改 | 路径策略的保存和加载 |
| 18 | `Resources.resx` | 修改 | 新增本地化字符串 |
| 19 | `features.md` | 修改 | 更新功能文档 |

**核心设计意图**:

##### 1. PermissionModel 扩展
- `PermissionLevel` 枚举增加 `AlwaysAsk` 值
- `PermissionLevels` 类提供两个属性：
  - `All` - 所有工具的权限级别（不含 AlwaysAsk）
  - `ForFileTools` - 文件工具的权限级别（含 AlwaysAsk）

##### 2. FileAccessType 枚举
- 定义 `Read`、`Write`、`ReadWrite` 三种访问类型
- 用于策略匹配时的兼容性判断

##### 3. PathPermissionPolicy 类
- `PolicyType` - 策略类型（Allow/Deny）
- `FileAccessType` - 访问类型
- `Pattern` - 通配符模式（构造时自动标准化）
- `Matches()` - 检查路径是否匹配
- `AppliesTo()` - 检查策略是否适用于请求的访问类型

##### 4. PathHelper 模块
- `NormalizePath()` - 路径标准化
  - 处理用户目录 (~)
  - 处理 POSIX 路径 (/d/projects -> D:\projects)
  - 转换为完整路径
- `LikePath()` - 通配符匹配（内部调用 NormalizePath）
- `PathPattern` 类 - 解析分号分隔的模式字符串，支持 ! 前缀排除

##### 5. IMcpPermissionHandler 接口
- 新增 `CheckFilePermission(featureName, operationDescription, filePath, accessType)` 方法

##### 6. 权限确认对话框

```
┌─────────────────────────────────────────────┐
│ 权限确认                                   │
├─────────────────────────────────────────────┤
│ [?] 是否允许以下操作？                       │
│     功能: Read Lines                        │
│     操作: 读取文件行                        │
├─────────────────────────────────────────────┤
│ 文件路径:                                   │
│ C:\Projects\MyProject\main.vb               │
├─────────────────────────────────────────────┤
│ ▶ 默认应答 [▼]                              │
│ 不分大小写，支持通配符：例如 * 和 ? [了解更多](https://learn.microsoft.com/en-us/dotnet/visual-basic/language-reference/operators/like-operator)                       │ │
│   ┌─────────────────────────────────────┐  │
│   │ ○ 总是允许                          │  │
│   │   C:\Projects\MyProject\main.vb     │  │
│   │ ○ 总是拒绝                          │  │
│   │   C:\Projects\MyProject\main.vb     │  │
│   │ ● 每次询问                          │  │
│   └─────────────────────────────────────┘  │
├─────────────────────────────────────────────┤
│              [允许]  [拒绝]                  │
└─────────────────────────────────────────────┘
```

- 显示功能名称、操作描述、文件路径
- Expander "默认应答"（默认折叠）：
  - 总是允许 + 可编辑的模式输入框
  - 总是拒绝 + 可编辑的模式输入框
  - 每次询问（默认选中）
- DialogResult 枚举：Allow/Deny/AllowWithPolicy/DenyWithPolicy

##### 7. MainWindow.Permissions 逻辑
- 维护 `_denyPolicies` 和 `_allowPolicies` 两个集合
- `CheckFilePermission()` - 功能权限检查（顶级）
- `CheckPathPoliciesOrAsk()` - Ask 模式下的路径策略检查
- `ShowFilePermissionDialog()` - 弹出对话框
- `AddPathPolicy()` - 添加新策略

##### 8. VisualStudioToolBase 扩展
- `IsFileTool` 虚属性 - 标识是否为文件操作工具
- `CheckFilePermission()` - 通过反射调用文件权限检查

##### 9. 文件操作工具修改
- 重写 `IsFileTool` 返回 `True`
- 执行前调用 `CheckFilePermission(filePath, accessType)`

##### 10. PermissionLevelConverter
- 实现 `IValueConverter` 接口
- `Convert()` - 将 `PermissionLevel` 枚举转换为资源文件中的本地化字符串
- `ConvertBack()` - 将本地化字符串转换回 `PermissionLevel` 枚举（用于下拉框编辑）
- 在 XAML 中声明为资源，绑定到 DataGridComboBoxColumn

##### 11. 路径策略配置 UI

```
┌─────────────────────────────────────────────────────────┐
│ 权限                                             │
├─────────────────────────────────────────────────────────┤
│ ┌─────────────────────────────────────────────────────┐ │
│ │ 功能权限                                            │ │
│ │ ┌─────────────────────────────────────────────────┐ │ │
│ │ │ 功能名称      │ 描述         │ 权限           │ │ │
│ │ ├─────────────────────────────────────────────────┤ │ │
│ │ │ read_lines    │ 读取文件行   │ [自动允许 ▼]   │ │ │
│ │ │ write_lines   │ 写入文件     │ [按需询问 ▼]   │ │ │
│ │ │ build_solution│ 构建解决方案 │ [按需询问 ▼]   │ │ │
│ │ │ string_replace│ 字符串替换   │ [总是询问 ▼]   │ │ │ ← 文件工具显示AlwaysAsk
│ │ └─────────────────────────────────────────────────┘ │ │
│ └─────────────────────────────────────────────────────┘ │
│                                                          │
│ ┌─────────────────────────────────────────────────────┐ │
│ │ Ask 模式自动授权策略                                 │ │
│ │ ┌─────────────────────────────────────────────────┐ │ │
│ │ │ [允许列表] [拒绝列表（优先）]                    │ │ │
│ │ ├─────────────────────────────────────────────────┤ │ │
│ │ │ 通配符模式           │ 访问类型   │              │ │ │
│ │ ├─────────────────────────────────────────────────┤ │ │
│ │ │ C:\Projects\*\*.vb   │ [读写 ▼]   │              │ │ │
│ │ │ ~\Documents\*.txt    │ [读取 ▼]   │              │ │ │
│ │ │ C:\Sensitive\*       │ [读写 ▼]   │              │ │ │
│ │ └─────────────────────────────────────────────────┘ │ │
│ │ [添加策略] [保存策略]                              │ │
│ │ • 拒绝列表优先于允许列表                           │ │
│ │ • 未匹配路径仍会弹出确认对话框                     │ │
│ │ • 不分大小写，支持通配符：例如 * 和 ? [了解更多](https://learn.microsoft.com/en-us/dotnet/visual-basic/language-reference/operators/like-operator)                       │ │
│ └─────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────┘
```

- 了解更多是按钮，点击事件里面导航，链接在 resx 里面，默认指向 en-us，中文指向 zh-cn
- 功能权限配置区域：
  - 权限列使用 `PermissionLevelConverter` 绑定，显示本地化字符串
  - 下拉框选项根据工具类型动态生成（文件工具多显示 AlwaysAsk）
- Ask 模式自动授权策略区域：
  - TabControl 分隔允许列表/拒绝列表
  - DataGrid 编辑策略（通配符模式 + 访问类型）
  - 访问类型列也使用 Converter 实现本地化（可选）
  - 添加/保存按钮
  - 说明文本

##### 12. 持久化
- `pathPolicies.xml` - 保存路径策略列表
- `permissions.xml` - 更新权限级别枚举值

##### 13. 本地化
| Key | zh-CN |
|-----|-------|
| PermissionLevel_Allow | 自动允许 |
| PermissionLevel_Ask | 按需询问 |
| PermissionLevel_AlwaysAsk | 总是询问 |
| PermissionLevel_Deny | 自动拒绝 |
| FileAccessType_Read | 读取 |
| FileAccessType_Write | 写入 |
| FileAccessType_ReadWrite | 读写 |

**使用场景**:

```
场景1：用户设置 C:\Projects\ 下所有 .vb 文件读写自动允许
→ 在允许列表添加策略：C:\Projects\*\*.vb，访问类型=读写
→ 后续访问匹配路径时自动允许

场景2：用户首次访问敏感文件 C:\Sensitive\config.txt
→ 弹出增强对话框
→ 用户选择"总是拒绝"，模式保持 C:\Sensitive\config.txt
→ 后续访问该文件自动拒绝

场景3：write_lines 工具设为 AlwaysAsk
→ 每次写操作都弹基础对话框（无 Expander）
→ 用户无法通过对话框快速添加策略
→ 确保高风险操作始终经过用户确认
```

**决策/理由**:
1. **功能权限是顶级控制** - 先判断工具级别的 Allow/Deny/Ask/AlwaysAsk
2. **路径策略服务于 Ask** - Ask 模式才检查路径策略，实现自动应答
3. **AlwaysAsk 完全绕过** - 适合高风险操作，确保每次都询问
4. **拒绝优先** - 安全性考虑
5. **PathHelper 合并功能** - 减少 文件数量，路径匹配和标准化紧密相关

**下一步**: 等待确认后调用 VbNetDev 智能体实现
