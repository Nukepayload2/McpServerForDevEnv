---
name: 文件权限单元测试扩展与重构
date: 2025-01-24
status: completed
related-features:
  - 权限管理系统
  - PathPermissionPolicy
  - FileAccessType
  - PathHelper
  - PathPolicyManager (新建)
---

# 文件权限单元测试扩展与重构

## 工作描述

扩展单元测试以覆盖文件权限功能，将策略管理逻辑从 UI 层抽取到独立的可测试类，消除冗余的数据结构，规范化测试文件命名。

### 核心目标

1. 抽取策略管理逻辑为独立的 PathPolicyManager 类
2. 消除冗余的 List 集合，只使用 ObservableCollection
3. 测试允许列表和拒绝列表的匹配逻辑
4. 测试文件访问类型判定逻辑
5. 测试策略添加、删除、去重逻辑
6. 规范化单元测试文件命名

## 相关上下文

### 架构相关
- 涉及项目: McpServiceNetFx.Core, McpServiceNetFx, McpServiceNetFx.Tests
- 相关模块: Models, Helpers, Views

### 功能相关
- 关联功能: 权限管理系统
- 源码位置:
  - `McpServiceNetFx.Core/Models/PathPermissionPolicy.vb`
  - `McpServiceNetFx.Core/Models/FileAccessType.vb`
  - `McpServiceNetFx.Core/Helpers/PathHelper.vb`
  - `McpServiceNetFx/Views/MainWindow.Permissions.vb` (待重构)

## 工作记录

### 2025-01-24 - 重构设计与测试计划

**状态**: completed

**执行结果**:

所有计划的代码和测试文件已完成实现并通过编译验证：

| 序号 | 文件 | 操作 | 状态 |
|------|------|------|------|
| 1 | `McpServiceNetFx.Core/Models/PolicyCheckResult.vb` | 新建 | ✅ 完成 |
| 2 | `McpServiceNetFx.Core/Helpers/PathPolicyManager.vb` | 新建 | ✅ 完成 |
| 3 | `McpServiceNetFx.Core/Mcp/IMcpPermissionHandler.vb` | 修改 | ✅ 完成 |
| 4 | `McpServiceNetFx/Views/MainWindow.Permissions.vb` | 修改 | ✅ 完成 |
| 5 | `McpServiceNetFx.Tests/Test1.vb` | 重命名 | ✅ 完成 |
| 6 | `McpServiceNetFx.Tests/PathPermissionPolicyTests.vb` | 新建 | ✅ 完成 |
| 7 | `McpServiceNetFx.Tests/PolicyCheckResultTests.vb` | 新建 | ✅ 完成 |
| 8 | `McpServiceNetFx.Tests/PathPolicyManagerTests.vb` | 新建 | ✅ 完成 |

**编译验证**:
- McpServiceNetFx.Core: ✅ 无错误无警告
- McpServiceNetFx.Tests: ✅ 无错误无警告
- McpServiceNetFx: ✅ 无错误无警告

**架构更新**:
- MaintainerAgent/subagent/architecture.md 已更新
- MaintainerAgent/subagent/features.md 已更新

**问题分析**:

当前 `MainWindow.Permissions.vb` 中存在数据冗余：

##### 现有代码结构（有冗余）
```
MainWindow.Permissions.vb
├── _allowPolicies (List)                    - 权限检查用
├── _denyPolicies (List)                     - 权限检查用
├── _allowPolicyItems (ObservableCollection) - UI 绑定用
├── _denyPolicyItems (ObservableCollection)  - UI 绑定用
├── CheckPathPoliciesOrAsk()   - 策略检查逻辑
├── AddPathPolicy()            - 添加策略逻辑（需同步两个集合）
└── LoadPathPolicies()         - 加载策略逻辑（需同步两个集合）
```

##### 冗余问题

| 问题 | 描述 |
|------|------|
| 数据重复 | List 和 ObservableCollection 存储相同数据 |
| 同步负担 | 添加/删除/加载时需要同步两个集合 |
| 代码冗余 | `AddPathPolicy` 中有重复的去重检查逻辑 |
| 难以测试 | 策略检查逻辑耦合在 UI 代码中 |

##### ObservableCollection 即可满足需求

- ObservableCollection 继承自 Collection，支持遍历和查询
- 可以直接用于权限检查，不需要额外的 List
- UI 绑定和权限检查共享同一个集合

**重构方案**:

##### 新建 PathPolicyManager 类

位置: `McpServiceNetFx.Core/Helpers/PathPolicyManager.vb`

```
PathPolicyManager
├── AllowPolicies (ObservableCollection) - 允许策略（UI绑定+权限检查）
├── DenyPolicies (ObservableCollection)  - 拒绝策略（UI绑定+权限检查）
├── AddPolicy(policyType, fileAccess, pattern) - 添加策略（自动去重）
├── RemovePolicy(policy)                 - 删除策略
├── ClearPolicies()                      - 清空所有策略
├── CheckPolicies(filePath, accessType)  - 检查策略匹配
└── LoadFrom/saveTo PersistenceModule    - 持久化接口
```

##### 核心方法伪代码

```
Function CheckPolicies(filePath As String, accessType As FileAccessType) As PolicyCheckResult
    ' 1. 先检查拒绝列表（优先级最高）
    For Each policy In DenyPolicies
        If policy.AppliesTo(accessType) AndAlso policy.Matches(filePath) Then
            Return PolicyCheckResult.Deny(policy)
        End If
    Next

    ' 2. 再检查允许列表
    For Each policy In AllowPolicies
        If policy.AppliesTo(accessType) AndAlso policy.Matches(filePath) Then
            Return PolicyCheckResult.Allow(policy)
        End If
    Next

    ' 3. 未匹配任何策略
    Return PolicyCheckResult.NoMatch
End Function

Sub AddPolicy(policyType As PathPolicyType, fileAccess As FileAccessType, pattern As String)
    Dim policy = New PathPermissionPolicy(policyType, fileAccess, pattern)
    Dim targetCollection = If(policyType = PathPolicyType.Allow, AllowPolicies, DenyPolicies)

    ' 去重：检查 pattern + fileAccess 组合是否已存在
    If Not targetCollection.Any(Function(p) p.Pattern = pattern AndAlso p.FileAccess = fileAccess) Then
        targetCollection.Add(policy)
    End If
End Sub
```

##### 新建 PolicyCheckResult 结构

位置: `McpServiceNetFx.Core/Models/PolicyCheckResult.vb`

```
PolicyCheckResult
├── Matched (Boolean)  - 是否匹配
├── IsAllowed (Boolean?) - 是否允许 (Nothing=未匹配, True=允许, False=拒绝)
├── MatchedPolicy (PathPermissionPolicy) - 匹配的策略
└── Shared Members: Allow(policy)/Deny(policy)/NoMatch 工厂方法
```

**修改文件清单**:

| 序号 | 文件 | 操作 | 设计意图 |
|------|------|------|----------|
| 1 | `McpServiceNetFx.Core/Models/PolicyCheckResult.vb` | 新建 | 策略检查结果模型 |
| 2 | `McpServiceNetFx.Core/Helpers/PathPolicyManager.vb` | 新建 | 策略管理器，独立可测试 |
| 3 | `McpServiceNetFx.Core/Mcp/IMcpPermissionHandler.vb` | 修改 | CheckFilePermission 改用 PolicyCheckResult |
| 4 | `McpServiceNetFx/Views/MainWindow.Permissions.vb` | 修改 | 使用 PathPolicyManager，删除冗余 List |
| 5 | `McpServiceNetFx.Tests/Test1.vb` | 重命名 | 规范化测试文件命名 |
| 6 | `McpServiceNetFx.Tests/PathPermissionPolicyTests.vb` | 新建 | PathPermissionPolicy 类测试 |
| 7 | `McpServiceNetFx.Tests/PolicyCheckResultTests.vb` | 新建 | PolicyCheckResult 类测试 |
| 8 | `McpServiceNetFx.Tests/PathPolicyManagerTests.vb` | 新建 | PathPolicyManager 类测试 |

**测试用例设计**:

##### PathPermissionPolicyTests.vb (现有计划保留)

| 测试区域 | 测试数量 |
|---------|---------|
| 构造函数 | 4 个 |
| Matches 方法 | 7 个 |
| AppliesTo 方法 | 9 个 |
| 属性 | 3 个 |
| 集成测试 | 3 个 |

##### PolicyCheckResultTests.vb (新增)

| 测试区域 | 测试内容 |
|---------|---------|
| 工厂方法 | Allow(policy), Deny(policy), NoMatch 创建正确结果 |
| 属性访问 | Matched, IsAllowed, MatchedPolicy 正确返回 |
| 未匹配场景 | NoMatch 时 Matched=False, IsAllowed=Nothing |
| 允许场景 | Allow 时 Matched=True, IsAllowed=True |
| 拒绝场景 | Deny 时 Matched=True, IsAllowed=False |

##### PathPolicyManagerTests.vb (新增)

| 测试区域 | 测试内容 |
|---------|---------|
| 构造函数 | 初始化空 ObservableCollection |
| AllowPolicies 属性 | 返回非空 ObservableCollection |
| DenyPolicies 属性 | 返回非空 ObservableCollection |
| AddPolicy - 允许策略 | 添加到 AllowPolicies，不添加到 DenyPolicies |
| AddPolicy - 拒绝策略 | 添加到 DenyPolicies，不添加到 AllowPolicies |
| AddPolicy - 去重 | pattern + fileAccess 相同时不重复添加 |
| AddPolicy - 触发事件 | ObservableCollection.CollectionChanged 触发 |
| RemovePolicy - 允许 | 从 AllowPolicies 删除 |
| RemovePolicy - 拒绝 | 从 DenyPolicies 删除 |
| ClearPolicies | 清空两个集合 |
| CheckPolicies - 拒绝优先 | 拒绝策略匹配时返回 Deny（忽略允许策略） |
| CheckPolicies - 允许匹配 | 允许策略匹配时返回 Allow |
| CheckPolicies - 未匹配 | 无匹配策略时返回 NoMatch |
| CheckPolicies - 访问类型判定 | Read/Write/ReadWrite 正确判定 |
| CheckPolicies - 空策略 | 空集合时返回 NoMatch |
| 集成测试 | 与 PathPermissionPolicy 完整集成 |

**UI 层简化**:

##### 修改前
```vb
Private _allowPolicies As New List(Of PathPermissionPolicy)
Private _denyPolicies As New List(Of PathPermissionPolicy)
Private _allowPolicyItems As New ObservableCollection(Of PathPermissionPolicy)()
Private _denyPolicyItems As New ObservableCollection(Of PathPermissionPolicy)()

Private Sub AddPathPolicy(...)
    ' 需要同时更新 List 和 ObservableCollection
    If policyType = PathPolicyType.Allow Then
        If Not _allowPolicies.Any(...) Then
            _allowPolicies.Add(policy)
        End If
        _allowPolicyItems.Add(policy)
    Else
        ' 同样的重复逻辑
    End If
End Sub
```

##### 修改后
```vb
Private _policyManager As New PathPolicyManager()

Private Sub AddPathPolicy(...)
    ' 直接调用管理器，去重和添加逻辑都在 PathPolicyManager
    _policyManager.AddPolicy(policyType, fileAccess, pattern)
End Sub

' XAML 绑定
<DgAllowPolicies.ItemsSource="{Binding _policyManager.AllowPolicies}" />
<DgDenyPolicies.ItemsSource="{Binding _policyManager.DenyPolicies}" />
```

**核心决策/理由**:

1. **消除数据冗余**: ObservableCollection 本身支持查询，不需要额外的 List
2. **单一数据源**: AllowPolicies/DenyPolicies 既是 UI 绑定源，也是权限检查源
3. **集中去重逻辑**: AddPolicy 内部统一处理去重，避免分散在多处
4. **策略检查逻辑抽取**: 将 `CheckPathPoliciesOrAsk` 的核心逻辑抽取到 `PathPolicyManager.CheckPolicies`
5. **拒绝优先原则**: 拒绝列表检查先于允许列表，确保安全性
6. **PolicyCheckResult**: 使用结果对象返回更多信息（匹配哪个策略）
7. **测试友好**: PathPolicyManager 不依赖 UI，可独立单元测试

**涉及文件**:
- `McpServiceNetFx.Core/Models/PolicyCheckResult.vb` (新建)
- `McpServiceNetFx.Core/Helpers/PathPolicyManager.vb` (新建)
- `McpServiceNetFx.Core/Mcp/IMcpPermissionHandler.vb` (修改)
- `McpServiceNetFx/Views/MainWindow.Permissions.vb` (修改 - 删除 _allowPolicies, _denyPolicies)
- `McpServiceNetFx.Tests/Test1.vb` (重命名)
- `McpServiceNetFx.Tests/PathPermissionPolicyTests.vb` (新建)
- `McpServiceNetFx.Tests/PolicyCheckResultTests.vb` (新建)
- `McpServiceNetFx.Tests/PathPolicyManagerTests.vb` (新建)
- `McpServiceNetFx.Tests/McpServiceNetFx.Tests.vbproj` (修改)

## 参考信息

- 相关文档: `MaintainerAgent/subagent/architecture.md`, `MaintainerAgent/subagent/features.md`
- 现有测试: `McpServiceNetFx.Tests/Test1.vb`
- MSTest 框架: `MSTestSettings.vb`
- 相关计划: `MaintainerAgent/works/file-permission-system-enhancement.md`
