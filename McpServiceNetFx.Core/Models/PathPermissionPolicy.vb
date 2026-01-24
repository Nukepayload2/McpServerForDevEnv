Imports System.IO

''' <summary>
''' 路径权限策略类型
''' </summary>
Public Enum PathPolicyType
    ''' <summary>
    ''' 允许策略
    ''' </summary>
    Allow

    ''' <summary>
    ''' 拒绝策略
    ''' </summary>
    Deny
End Enum

''' <summary>
''' 路径权限策略
''' 用于配置文件操作的自动应答规则
''' </summary>
Public Class PathPermissionPolicy
    ''' <summary>
    ''' 策略类型（允许或拒绝）
    ''' </summary>
    Public Property PolicyType As PathPolicyType

    ''' <summary>
    ''' 文件访问类型
    ''' </summary>
    Public Property FileAccess As FileAccessType

    ''' <summary>
    ''' 通配符模式（原始形式，不标准化）
    ''' </summary>
    Public Property Pattern As String

    ''' <summary>
    ''' 创建路径权限策略
    ''' </summary>
    ''' <param name="policyType">策略类型</param>
    ''' <param name="fileAccess">文件访问类型</param>
    ''' <param name="pattern">通配符模式（保持原始形式，允许空字符串用于UI编辑场景）</param>
    Public Sub New(policyType As PathPolicyType, fileAccess As FileAccessType, pattern As String)
        ' 允许空 Pattern 以支持 UI 编辑场景（用户先添加策略再填写内容）
        ' 验证将在实际使用时（Matches 方法）进行
        Me.PolicyType = policyType
        Me.FileAccess = fileAccess
        Me.Pattern = pattern
    End Sub

    ''' <summary>
    ''' 检查路径是否匹配此策略
    ''' </summary>
    ''' <param name="filePath">要检查的文件路径</param>
    ''' <returns>如果路径匹配策略模式则返回 True</returns>
    Public Function Matches(filePath As String) As Boolean
        ' 空 Pattern 不匹配任何路径（用于支持 UI 编辑场景）
        If String.IsNullOrWhiteSpace(Pattern) Then
            Return False
        End If

        If String.IsNullOrWhiteSpace(filePath) Then
            Return False
        End If

        Dim normalizedPath = PathHelper.NormalizePath(filePath)
        Return normalizedPath Like Pattern
    End Function

    ''' <summary>
    ''' 检查策略是否适用于请求的访问类型
    ''' </summary>
    ''' <param name="requestedAccess">请求的访问类型</param>
    ''' <returns>如果策略适用于请求的访问类型则返回 True</returns>
    ''' <remarks>
    ''' 匹配规则：
    ''' - ReadWrite 策略适用于所有访问类型
    ''' - Read 策略适用于 Read 和 ReadWrite 请求（因为 ReadWrite 包含 Read）
    ''' - Write 策略适用于 Write 和 ReadWrite 请求（因为 ReadWrite 包含 Write）
    ''' </remarks>
    Public Function AppliesTo(requestedAccess As FileAccessType) As Boolean
        Select Case FileAccess
            Case FileAccessType.ReadWrite
                ' 读写策略适用于所有访问类型
                Return True

            Case FileAccessType.Read
                ' 读策略适用于读请求和读写请求
                Return requestedAccess = FileAccessType.Read OrElse requestedAccess = FileAccessType.ReadWrite

            Case FileAccessType.Write
                ' 写策略适用于写请求和读写请求
                Return requestedAccess = FileAccessType.Write OrElse requestedAccess = FileAccessType.ReadWrite

            Case Else
                Return False
        End Select
    End Function
End Class
