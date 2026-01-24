---
name: 权限 UI TabControl 重构
date: 2026-01-24
status: active
related-features:
  - 权限控制
  - UI 配置
---

# 权限 UI TabControl 重构

## 工作描述

将"MCP 功能授权"选项卡的内容结构改为内部 TabControl，分为"功能权限"和"文件权限"两个子标签页，以更好地组织权限配置界面，解决当前界面混排、需滚动查看所有内容的问题。

## 相关上下文

### 架构相关
- 涉及项目: McpServiceNetFx, McpServiceNetFx.Core
- 相关模块:
  - Views 模块（主窗口 UI）
  - Resources 模块（本地化资源）

### 功能相关
- 关联功能: 权限控制
- 源码位置:
  - UI 定义: `McpServiceNetFx\Views\MainWindow.xaml` (第134-310行)
  - 事件处理: `McpServiceNetFx\Views\MainWindow.Permissions.vb`
  - 英文资源: `McpServiceNetFx.Core\My Project\Resources.resx`
  - 中文资源: `McpServiceNetFx.Core\My Project\Resources.zh-CN.resx`

## 当前 UI 结构

```
┌─────────────────────────────────────────────────┐
│  MCP 功能授权 (TabItem)                          │
│  ┌───────────────────────────────────────────┐  │
│  │ ScrollViewer                              │  │
│  │ ┌─────────────────────────────────────┐   │  │
│  │ │ StackPanel                          │   │  │
│  │ │                                     │   │  │
│  │ │ ┌───────────────────────────────┐   │   │  │
│  │ │ │ GroupBox: 功能权限            │   │   │  │
│  │ │ │ - 允许全部/询问全部按钮       │   │   │  │
│  │ │ │ - 功能权限配置 DataGrid       │   │   │  │
│  │ │ └───────────────────────────────┘   │   │  │
│  │ │                                     │   │  │
│  │ │ ┌───────────────────────────────┐   │   │  │
│  │ │ │ GroupBox: Ask 模式自动授权    │   │   │  │
│  │ │ │ - 允许列表/拒绝列表 TabControl│   │   │  │
│  │ │ │ - 提示文本                    │   │   │  │
│  │ │ │ - 添加策略按钮                │   │   │  │
│  │ │ └───────────────────────────────┘   │   │  │
│  │ │                                     │   │  │
│  │ │ ┌───────────────────────────────┐   │   │  │
│  │ │ │ 保存和重新加载按钮            │   │   │  │
│  │ │ └───────────────────────────────┘   │   │  │
│  │ └─────────────────────────────────────┘   │  │
│  └───────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

**问题**:
- 功能权限和文件权限混在一个页面上，没有清晰区分
- 用户需要滚动才能看到所有内容
- 视觉层次不够清晰

## 目标 UI 结构

```
┌─────────────────────────────────────────────────┐
│  MCP 功能授权 (TabItem)                          │
│  ┌───────────────────────────────────────────┐  │
│  │ Grid (行1: *)                              │  │
│  │ ┌─────────────────────────────────────┐   │  │
│  │ │ 内部 TabControl (TabPermissionTypes)│   │  │
│  │ │                                     │   │  │
│  │ │ ┌─ 功能权限 ─┬─ 文件权限 ────┐     │   │  │
│  │ │ │            │               │     │   │  │
│  │ │ │ [允许全部] │ ┌─允许─┬─拒绝─┐│     │   │  │
│  │ │ │ [询问全部] │ │列表  │列表  ││     │   │  │
│  │ │ │            │ ├──────┼──────┤│     │   │  │
│  │ │ │ DataGrid   │ │Data  │Data  ││     │   │  │
│  │ │ │ (功能权限) │ │Grid  │Grid  ││     │   │  │
│  │ │ │            │ └──────┴──────┘│     │   │  │
│  │ │ │            │               │     │   │  │
│  │ │ │            │ 提示文本区域  │     │   │  │
│  │ │ │            │ [添加策略]    │     │   │  │
│  │ │ └────────────┴───────────────┘     │   │  │
│  │ └─────────────────────────────────────┘   │  │
│  └───────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────┐  │
│  │ Grid (行2: Auto)                            │  │
│  │ [保存配置和策略] [重新加载]                 │  │
│  └───────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

## 工作记录

### 2026-01-24 - 创建工作记忆

**状态**: pending

**描述**:
规划权限 UI 重构工作，将"MCP 功能授权"选项卡从单页面结构改为内部 TabControl 分页结构。

**涉及文件**:
- `McpServiceNetFx\Views\MainWindow.xaml` (修改)
- `McpServiceNetFx\Views\MainWindow.Permissions.vb` (无需修改)
- `McpServiceNetFx.Core\My Project\Resources.resx` (新增资源)
- `McpServiceNetFx.Core\My Project\Resources.zh-CN.resx` (新增资源)

**决策/理由**:
- 使用内部 TabControl 分离功能权限和文件权限，提升界面清晰度
- 移除 ScrollViewer，改用 Grid 布局，避免滚动
- 保持所有控件名称不变，确保无需修改后端代码

**下一步**:
等待开始实施

### [已完成] - 修改资源文件

**状态**: completed

**描述**:
在资源文件中新增两个标签页标题的资源条目。

**需要新增的资源**:
- `PermissionType_Function`: "Function Permissions" / "功能权限"
- `PermissionType_File`: "File Permissions" / "文件权限"

**修改文件**:
- `Resources.resx` (英文) - 已添加
- `Resources.zh-CN.resx` (中文) - 已添加

**操作步骤**:
1. 在 `Resources.resx` 的 `</root>` 标签前添加新资源条目 - 已完成
2. 在 `Resources.zh-CN.resx` 中添加对应中文翻译 - 已完成
3. 在 Visual Studio 中对 `McpServiceNetFx.Core` 项目运行"运行自定义工具" - 待用户手动执行
4. 重新生成 Resources.Designer.vb - 待用户手动执行

**下一步**:
修改 MainWindow.xaml 文件

### [已完成] - 修改 MainWindow.xaml

**状态**: completed

**描述**:
重构"MCP 功能授权"选项卡的 XAML 结构。

**修改范围**: 第134-310行

**主要变更**:
1. 移除外层 ScrollViewer，改用 Grid 布局 - 已完成
2. 添加 Grid.RowDefinitions (行1: *, 行2: Auto) - 已完成
3. 创建内部 TabControl (x:Name="TabPermissionTypes") - 已完成
4. 将原功能权限 GroupBox 内容移入"功能权限" Tab - 已完成
5. 将原文件权限 GroupBox 内容移入"文件权限" Tab - 已完成
6. 将保存按钮区域移至 Grid.Row="1" - 已完成

**布局伪代码结构**:
```
TabItem (MCP 功能授权)
  └─ Grid
      ├─ Row 0 (Height="*")
      │   └─ TabControl (TabPermissionTypes)
      │       ├─ TabItem (功能权限)
      │       │   └─ StackPanel
      │       │       ├─ StackPanel (Horizontal)
      │       │       │   ├─ Button (BtnAllowAll)
      │       │       │   └─ Button (BtnAskAll)
      │       │       └─ DataGrid (DgPermissions)
      │       └─ TabItem (文件权限)
      │           └─ Grid
      │               ├─ Row 0 (Auto) - TabControl (TabPathPolicies)
      │               ├─ Row 1 (Auto) - 提示文本 StackPanel
      │               └─ Row 2 (Auto) - Button (BtnAddPolicy)
      └─ Row 1 (Height="Auto")
          └─ StackPanel (Horizontal)
              ├─ Button (BtnSavePermissions)
              └─ Button (BtnReloadPermissions)
```

**下一步**:
验证编译

### [待执行] - 验证编译和测试

**状态**: pending

**描述**:
编译解决方案并运行功能测试。

**验证步骤**:
1. 在 Visual Studio 中生成解决方案
2. 确认没有编译错误
3. 运行 McpServiceNetFx 应用程序
4. 打开"MCP 功能授权"选项卡
5. 验证内部 TabControl 正常显示
6. 验证"功能权限"和"文件权限"两个 Tab 切换正常
7. 验证所有按钮功能正常

**功能完整性检查清单**:
- [ ] 功能权限 DataGrid 显示正常
- [ ] 权限级别下拉框正常工作
- [ ] 允许列表/拒绝列表 Tab 切换正常
- [ ] 策略 DataGrid 编辑功能正常
- [ ] 添加策略按钮功能正常
- [ ] 保存配置和策略功能正常
- [ ] 重新加载功能正常
- [ ] 了解更多链接可正常打开

**下一步**:
更新 MaintainerAgent 知识缓存

### [待执行] - 更新 MaintainerAgent

**状态**: pending

**描述**:
重构完成后，更新 MaintainerAgent 的知识缓存。

**需要更新的文件**:
- `MaintainerAgent/subagent/features.md`

**更新内容**:
在"权限控制"章节的"UI 配置位置"部分，说明"MCP 功能授权"选项卡现在使用内部 TabControl 分为功能权限和文件权限两个子标签页。

**下一步**:
工作完成

## 参考信息

### 复用的现有资源
以下资源键已存在，无需新增:
- `ButtonAllowAll` - 全部允许/Allow All
- `ButtonAskAll` - 全部询问/Ask All
- `ButtonSaveConfigAndPolicies` - 保存配置和策略
- `ButtonReload` - 重新加载
- `PathPolicy_AllowList` - 允许列表
- `PathPolicy_DenyList` - 拒绝列表
- `PathPolicy_Column_Pattern` - 通配符模式
- `PathPolicy_Column_AccessType` - 访问类型
- `PathPolicy_Button_AddPolicy` - 添加策略
- `PathPolicy_Hint_*` - 各种提示文本
- `PermissionConfirm_LearnMore` - 了解更多

### 注意事项
1. **事件处理程序**: 无需修改 `MainWindow.Permissions.vb`，所有按钮名称和事件绑定保持不变
2. **DataGrid 名称**: 保持 `DgPermissions`、`DgAllowPolicies`、`DgDenyPolicies`、`TabPathPolicies` 不变
3. **资源键名**: 新增的资源键名需要与现有命名风格保持一致
4. **布局高度**: 移除 ScrollViewer 后，确保内部 TabControl 有足够的高度显示内容

### 回滚方案
如果重构后出现问题:
1. 使用 Git 回滚 `MainWindow.xaml` 文件
2. 删除资源文件中新增的两个条目
3. 运行自定义工具重新生成 Resources.Designer.vb
