---
name: 路径策略 UI 同步修复
date: 2025-01-25
status: completed
related-features:
  - 权限管理系统
  - 路径通配符策略
---

# 路径策略 UI 同步修复

## 工作描述

修复用户在权限确认对话框中添加"总是允许"/"总是拒绝"策略后，策略未出现在文件权限列表中的问题。

## 相关上下文

### 架构相关
- 涉及项目: McpServiceNetFx, McpServiceNetFx.Core
- 相关模块: Helpers, Views

### 功能相关
- 关联功能: 路径通配符策略
- 源码位置:
  - 模型: McpServiceNetFx.Core/Helpers/PathPolicyManager.vb
  - UI: McpServiceNetFx/Views/MainWindow.Permissions.vb
  - UI: McpServiceNetFx/Views/MainWindow.xaml

## 工作记录

### 2025-01-25 - 问题定位与修复

**状态**: completed

**问题描述**:
用户在权限确认对话框中点击"总是允许"或"总是拒绝"后，添加的路径策略没有出现在文件权限列表中。

**根因分析**:
当前设计维护了两个集合：
1. `PathPolicyManager.AllowPolicies` - 用于权限检查
2. `_allowPolicyItems` - 用于 UI 绑定

通过对话框添加策略时只更新了 `PathPolicyManager.AllowPolicies`，UI 绑定的 `_allowPolicyItems` 没有同步更新。

**修复方案**:
采用单一数据源设计：
- `PathPolicyManager` 的集合已使用 `ObservableCollection(Of PathPermissionPolicy)`
- 移除 `MainWindow.Permissions` 中的中间集合 `_allowPolicyItems`/`_denyPolicyItems`
- UI 直接绑定到 `_pathPolicyManager.AllowPolicies` 和 `_pathPolicyManager.DenyPolicies`
- 利用 `ObservableCollection` 的 `CollectionChanged` 事件自动更新 UI

**修改文件清单**:

| 序号 | 文件 | 操作 | 修改内容 |
|------|------|------|----------|
| 1 | `McpServiceNetFx/Views/MainWindow.Permissions.vb` | 修改 | 移除 `_allowPolicyItems`/`_denyPolicyItems` 字段 |
| 2 | `McpServiceNetFx/Views/MainWindow.Permissions.vb` | 修改 | `LoadPathPolicies` 方法直接绑定到 `_pathPolicyManager` 集合 |
| 3 | `McpServiceNetFx/Views/MainWindow.Permissions.vb` | 修改 | `BtnAddPolicy_Click` 方法移除重复集合操作 |

**架构变更**:

```
Before (Dual Collections):
┌─────────────────────────────────────────┐
│           MainWindow.Permissions.vb      │
│  ┌─────────────────────────────────┐    │
│  │ _allowPolicyItems (UI Binding)  │    │
│  │ _denyPolicyItems  (UI Binding)  │    │
│  └─────────────────────────────────┘    │
│                  ▲                       │
│                  │ (manual sync)         │
│                  ▼                       │
│  ┌─────────────────────────────────┐    │
│  │ PathPolicyManager.AllowPolicies │    │
│  │ PathPolicyManager.DenyPolicies  │    │
│  └─────────────────────────────────┘    │
└─────────────────────────────────────────┘

After (Single Data Source):
┌─────────────────────────────────────────┐
│           MainWindow.Permissions.vb      │
│                                         │
│  ┌─────────────────────────────────┐    │
│  │ PathPolicyManager.AllowPolicies │◄───┤ UI Direct Binding
│  │ PathPolicyManager.DenyPolicies  │◄───┤ (ObservableCollection)
│  └─────────────────────────────────┘    │
└─────────────────────────────────────────┘
```

**决策/理由**:
1. **单一数据源** - 避免数据不一致
2. **自动同步** - `ObservableCollection` 自动通知 UI 更新
3. **简化代码** - 移除手动同步逻辑

**编译验证**: ✅ McpServiceNetFx.Core 和 McpServiceNetFx 编译成功，0 警告 0 错误

## 参考信息

- 相关文档: MaintainerAgent/works/file-permission-system-enhancement.md
- 相关文档: MaintainerAgent/works/remove-reflection-permission-check.md
