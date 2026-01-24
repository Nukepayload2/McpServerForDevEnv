---
name: Path Policy UI Fix
date: 2025-01-24
status: completed
related-features:
  - Path Permission Policy System
  - Permission Management UI
---

# Path Policy UI Fix

## 工作描述

修复添加路径策略时的默认值问题：
- 将默认 Pattern 从 "C:\*\*" 改为空字符串
- 在 DataGrid 的 Pattern 列添加水印/占位符提示文本

## 相关上下文

### 架构相关
- 涉及项目: McpServiceNetFx, McpServiceNetFx.Core
- 相关模块: Permissions (权限管理), UI (主窗口)

### 功能相关
- 关联功能: Path Permission Policy Management
- 源码位置:
  - G:\Projects\McpServerForDevEnv\McpServiceNetFx\Views\MainWindow.Permissions.vb
  - G:\Projects\McpServerForDevEnv\McpServiceNetFx\Views\MainWindow.xaml
  - G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\My Project\Resources.resx
  - G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\My Project\Resources.zh-CN.resx

## 工作记录

### 2025-01-24 - 修复默认值和添加水印提示

**状态**: completed

**描述**:
用户报告添加路径策略时默认通配符模式显示为 "C:\*\*"，这应该是提示文本而不是默认值。修复方案：
1. 修改 BtnAddPolicy_Click 方法，将默认 Pattern 改为 String.Empty
2. 在 MainWindow.xaml 中添加 WatermarkTextBoxStyle 样式
3. 为允许列表和拒绝列表的 DataGridTextColumn 应用水印样式
4. 在资源文件中添加 PathPolicy_Pattern_Placeholder 资源键

**涉及文件**:
- G:\Projects\McpServerForDevEnv\McpServiceNetFx\Views\MainWindow.Permissions.vb (第 365、366、370、371 行)
- G:\Projects\McpServerForDevEnv\McpServiceNetFx\Views\MainWindow.xaml (第 13-46 行，第 201-214 行，第 242-255 行)
- G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\My Project\Resources.resx (第 1083-1086 行)
- G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\My Project\Resources.zh-CN.resx (第 1084-1087 行)

**决策/理由**:
1. 使用空字符串作为默认值，让用户必须显式输入有效的通配符模式
2. 水印提示显示 "e.g. C:\Projects\*\*.vb" (英文) / "例如：C:\\Projects\\*\*.vb" (中文)
3. 水印只在文本框为空且失去焦点时显示，输入时自动隐藏

**下一步**:
- VSIX 项目 (McpServiceNetFx.VsixAsync) 暂未实现路径策略功能，无需修复

### 2025-01-24 - 修复空 Pattern 验证问题

**状态**: completed

**描述**:
修复构造函数中的参数验证冲突。当 UI 添加空 Pattern 策略时，`PathPermissionPolicy` 构造函数抛出 `ArgumentException: "Pattern cannot be empty"`。

解决方案：
1. 从构造函数中移除空 Pattern 验证，允许创建空 Pattern 的策略对象（支持 UI 编辑场景）
2. 在 `Matches()` 方法中添加空 Pattern 检查，空 Pattern 返回 False（不匹配任何路径）
3. 更新构造函数注释说明允许空字符串用于 UI 编辑场景

**涉及文件**:
- G:\Projects\McpServerForDevEnv\McpServiceNetFx.Core\Models\PathPermissionPolicy.vb
  - 第 38-50 行：修改构造函数，移除空值验证
  - 第 52-69 行：修改 Matches() 方法，添加空 Pattern 检查

**决策/理由**:
1. **验证时机选择**：构造时允许空 Pattern，使用时（Matches）才验证
2. **UI 场景支持**：用户可以先添加策略再填写内容，提升用户体验
3. **安全性保障**：空 Pattern 永远不匹配任何路径，不会产生误判
4. **符合延迟验证原则**：只在真正需要时才验证，避免过早验证阻止合法操作

## 参考信息

- 相关文档: Visual Basic LIKE Operator (https://learn.microsoft.com/en-us/dotnet/visual-basic/language-reference/operators/like-operator)
- 相关讨论: 用户反馈 UI 体验问题，默认值容易误导用户
