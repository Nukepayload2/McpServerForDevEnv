Imports System.Collections.ObjectModel
Imports System.Collections.Specialized

''' <summary>
''' 路径权限策略管理器，集中管理允许和拒绝策略
''' </summary>
Public Class PathPolicyManager
    Private _allowPolicies As New ObservableCollection(Of PathPermissionPolicy)
    Private _denyPolicies As New ObservableCollection(Of PathPermissionPolicy)

    Public Sub New()
        ' 初始化为空集合
    End Sub

    ''' <summary>
    ''' 允许策略集合（UI 绑定 + 权限检查）
    ''' </summary>
    Public ReadOnly Property AllowPolicies As ObservableCollection(Of PathPermissionPolicy)
        Get
            Return _allowPolicies
        End Get
    End Property

    ''' <summary>
    ''' 拒绝策略集合（UI 绑定 + 权限检查）
    ''' </summary>
    Public ReadOnly Property DenyPolicies As ObservableCollection(Of PathPermissionPolicy)
        Get
            Return _denyPolicies
        End Get
    End Property

    ''' <summary>
    ''' 添加策略（自动去重）
    ''' </summary>
    Public Sub AddPolicy(policyType As PathPolicyType, fileAccess As FileAccessType, pattern As String)
        Dim policy = New PathPermissionPolicy(policyType, fileAccess, pattern)
        Dim targetCollection = If(policyType = PathPolicyType.Allow, _allowPolicies, _denyPolicies)

        ' 去重：检查 pattern + fileAccess 组合是否已存在
        If Not targetCollection.Any(Function(p) p.Pattern = pattern AndAlso p.FileAccess = fileAccess) Then
            targetCollection.Add(policy)
        End If
    End Sub

    ''' <summary>
    ''' 删除策略
    ''' </summary>
    Public Sub RemovePolicy(policy As PathPermissionPolicy)
        If _allowPolicies.Contains(policy) Then
            _allowPolicies.Remove(policy)
        End If
        If _denyPolicies.Contains(policy) Then
            _denyPolicies.Remove(policy)
        End If
    End Sub

    ''' <summary>
    ''' 清空所有策略
    ''' </summary>
    Public Sub ClearPolicies()
        _allowPolicies.Clear()
        _denyPolicies.Clear()
    End Sub

    ''' <summary>
    ''' 检查策略匹配（拒绝优先）
    ''' </summary>
    Public Function CheckPolicies(filePath As String, accessType As FileAccessType) As PolicyCheckResult
        ' 1. 先检查拒绝列表（优先级最高）
        For Each policy In _denyPolicies
            If policy.AppliesTo(accessType) AndAlso policy.Matches(filePath) Then
                Return PolicyCheckResult.Deny(policy)
            End If
        Next

        ' 2. 再检查允许列表
        For Each policy In _allowPolicies
            If policy.AppliesTo(accessType) AndAlso policy.Matches(filePath) Then
                Return PolicyCheckResult.Allow(policy)
            End If
        Next

        ' 3. 未匹配任何策略
        Return PolicyCheckResult.NoMatch
    End Function
End Class
