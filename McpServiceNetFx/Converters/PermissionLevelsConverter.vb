Imports System.Globalization
Imports System.Windows.Data
Imports McpServiceNetFx

Namespace Converters
    ''' <summary>
    ''' 权限级别集合转换器
    ''' 根据 PermissionItem 的 IsFileTool 属性返回对应的权限级别集合
    ''' </summary>
    Public Class PermissionLevelsConverter
        Implements IMultiValueConverter

        Public Function Convert(values As Object(), targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IMultiValueConverter.Convert
            If values Is Nothing OrElse values.Length < 1 Then
                Return Nothing
            End If

            ' values(0) 是 PermissionItem 对象
            Dim item = TryCast(values(0), PermissionItem)
            If item Is Nothing Then
                ' 如果转换失败，返回默认的所有权限级别
                Return PermissionLevels.All
            End If

            ' 根据工具类型返回对应的权限级别集合
            Return item.AvailablePermissionLevels
        End Function

        Public Function ConvertBack(value As Object, targetTypes As Type(), parameter As Object, culture As CultureInfo) As Object() Implements IMultiValueConverter.ConvertBack
            Throw New NotImplementedException()
        End Function
    End Class
End Namespace
