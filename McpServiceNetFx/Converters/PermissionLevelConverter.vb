Imports System.Globalization
Imports System.Windows.Data
Imports McpServiceNetFx

Namespace Converters
    ''' <summary>
    ''' 权限级别转换器
    ''' 用于在 UI 中显示本地化的权限级别字符串
    ''' </summary>
    Public Class PermissionLevelConverter
        Implements IValueConverter

    ''' <summary>
    ''' 将 PermissionLevel 枚举转换为本地化字符串
    ''' </summary>
    Public Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
        If Not TypeOf value Is PermissionLevel Then
            Return Nothing
        End If

        Dim permissionLevel = DirectCast(value, PermissionLevel)

        Select Case permissionLevel
            Case PermissionLevel.Allow
                Return My.Resources.PermissionLevel_Allow
            Case PermissionLevel.Ask
                Return My.Resources.PermissionLevel_Ask
            Case PermissionLevel.AlwaysAsk
                Return My.Resources.PermissionLevel_AlwaysAsk
            Case PermissionLevel.Deny
                Return My.Resources.PermissionLevel_Deny
            Case Else
                Return permissionLevel.ToString()
        End Select
    End Function

    ''' <summary>
    ''' 将本地化字符串转换回 PermissionLevel 枚举
    ''' </summary>
    Public Function ConvertBack(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
        If Not TypeOf value Is String Then
            Return Nothing
        End If

        Dim stringValue = DirectCast(value, String)

        ' 将本地化字符串映射到枚举值
        If stringValue.Equals(My.Resources.PermissionLevel_Allow, StringComparison.Ordinal) Then
            Return PermissionLevel.Allow
        ElseIf stringValue.Equals(My.Resources.PermissionLevel_Ask, StringComparison.Ordinal) Then
            Return PermissionLevel.Ask
        ElseIf stringValue.Equals(My.Resources.PermissionLevel_AlwaysAsk, StringComparison.Ordinal) Then
            Return PermissionLevel.AlwaysAsk
        ElseIf stringValue.Equals(My.Resources.PermissionLevel_Deny, StringComparison.Ordinal) Then
            Return PermissionLevel.Deny
        End If

        ' 尝试直接解析枚举名称
        Dim [enum] As PermissionLevel
        If [Enum].TryParse(stringValue, [enum]) Then
            Return [enum]
        End If

        Return Nothing
    End Function
    End Class
End Namespace
