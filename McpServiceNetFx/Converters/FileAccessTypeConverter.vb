Imports System.Globalization
Imports System.Windows.Data
Imports McpServiceNetFx.Models

Namespace Converters
    ''' <summary>
    ''' 文件访问类型转换器
    ''' 用于在 UI 中显示本地化的文件访问类型字符串
    ''' </summary>
    Public Class FileAccessTypeConverter
        Implements IValueConverter

    ''' <summary>
    ''' 将 FileAccessType 枚举转换为本地化字符串
    ''' </summary>
    Public Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
        If Not TypeOf value Is FileAccessType Then
            Return Nothing
        End If

        Dim accessType = DirectCast(value, FileAccessType)

        Select Case accessType
            Case FileAccessType.Read
                Return My.Resources.FileAccessType_Read
            Case FileAccessType.Write
                Return My.Resources.FileAccessType_Write
            Case FileAccessType.ReadWrite
                Return My.Resources.FileAccessType_ReadWrite
            Case Else
                Return accessType.ToString()
        End Select
    End Function

    ''' <summary>
    ''' 将本地化字符串转换回 FileAccessType 枚举
    ''' </summary>
    Public Function ConvertBack(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
        If Not TypeOf value Is String Then
            Return Nothing
        End If

        Dim stringValue = DirectCast(value, String)

        ' 将本地化字符串映射到枚举值
        If stringValue.Equals(My.Resources.FileAccessType_Read, StringComparison.Ordinal) Then
            Return FileAccessType.Read
        ElseIf stringValue.Equals(My.Resources.FileAccessType_Write, StringComparison.Ordinal) Then
            Return FileAccessType.Write
        ElseIf stringValue.Equals(My.Resources.FileAccessType_ReadWrite, StringComparison.Ordinal) Then
            Return FileAccessType.ReadWrite
        End If

        ' 尝试直接解析枚举名称
        Dim [enum] As FileAccessType
        If [Enum].TryParse(stringValue, [enum]) Then
            Return [enum]
        End If

        Return Nothing
    End Function
    End Class
End Namespace
