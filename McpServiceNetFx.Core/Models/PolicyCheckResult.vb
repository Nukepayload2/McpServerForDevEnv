''' <summary>
''' 策略检查结果，包含匹配状态和匹配的策略
''' </summary>
Public Structure PolicyCheckResult
    Private _matched As Boolean
    Private _isAllowed As Boolean?
    Private _matchedPolicy As PathPermissionPolicy

    Public Sub New(matched As Boolean, isAllowed As Boolean?, matchedPolicy As PathPermissionPolicy)
        _matched = matched
        _isAllowed = isAllowed
        _matchedPolicy = matchedPolicy
    End Sub

    Public ReadOnly Property Matched As Boolean
        Get
            Return _matched
        End Get
    End Property

    ''' <summary>
    ''' 是否允许访问 (Nothing=未匹配, True=允许, False=拒绝)
    ''' </summary>
    Public ReadOnly Property IsAllowed As Boolean?
        Get
            Return _isAllowed
        End Get
    End Property

    Public ReadOnly Property MatchedPolicy As PathPermissionPolicy
        Get
            Return _matchedPolicy
        End Get
    End Property

    ''' <summary>
    ''' 创建允许结果
    ''' </summary>
    Public Shared Function Allow(policy As PathPermissionPolicy) As PolicyCheckResult
        Return New PolicyCheckResult(True, True, policy)
    End Function

    ''' <summary>
    ''' 创建拒绝结果
    ''' </summary>
    Public Shared Function Deny(policy As PathPermissionPolicy) As PolicyCheckResult
        Return New PolicyCheckResult(True, False, policy)
    End Function

    ''' <summary>
    ''' 创建未匹配结果
    ''' </summary>
    Public Shared ReadOnly Property NoMatch As PolicyCheckResult
        Get
            Return New PolicyCheckResult(False, Nothing, Nothing)
        End Get
    End Property
End Structure
