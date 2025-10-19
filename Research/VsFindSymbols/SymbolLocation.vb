''' <summary>
''' 搜索范围枚举
''' </summary>
Public Enum SearchScope
    Solution
    Project
    [Namespace]
    [Class]
End Enum

''' <summary>
''' 搜索类型枚举
''' </summary>
Public Enum SearchType
    Definition
    Reference
    All
End Enum

''' <summary>
''' 符号位置信息类
''' </summary>
Public Class SymbolLocation
    Public Property SymbolName As String
    Public Property FilePath As String
    Public Property LineNumber As Integer
    Public Property ColumnNumber As Integer
    Public Property ProjectName As String
    Public Property SymbolType As String
    Public Property Index As Integer
    Public Property ToolTip As String
    Public Property SymbolNamespace As String
    Public Property ProjectType As String
    Public Property LibraryGuid As Guid
    Public Property ProjectGuid As Guid

    ' 只读属性
    Public ReadOnly Property FileName As String
        Get
            If String.IsNullOrWhiteSpace(FilePath) Then
                Return ""
            End If
            Return IO.Path.GetFileName(FilePath)
        End Get
    End Property

    Public ReadOnly Property Directory As String
        Get
            If String.IsNullOrWhiteSpace(FilePath) Then
                Return ""
            End If
            Return IO.Path.GetDirectoryName(FilePath)
        End Get
    End Property

    Public ReadOnly Property FullDisplayName As String
        Get
            Dim sb As New Text.StringBuilder()
            sb.Append(SymbolType)
            sb.Append(" ")
            sb.Append(SymbolName)

            If Not String.IsNullOrWhiteSpace(SymbolNamespace) Then
                sb.Append(" (")
                sb.Append(SymbolNamespace)
                sb.Append(")")
            End If

            If Not String.IsNullOrWhiteSpace(FilePath) Then
                sb.Append(" 在 ")
                sb.Append(FilePath)
                If LineNumber > 0 Then
                    sb.Append(":")
                    sb.Append(LineNumber)
                    If ColumnNumber > 0 Then
                        sb.Append(":")
                        sb.Append(ColumnNumber)
                    End If
                End If
            End If

            Return sb.ToString()
        End Get
    End Property

    Public Overrides Function ToString() As String
        If String.IsNullOrWhiteSpace(FilePath) Then
            Return $"{SymbolType} {SymbolName} (项目: {ProjectName})"
        End If

        Return $"{SymbolType} {SymbolName} 在 {FilePath}:{LineNumber}:{ColumnNumber} (项目: {ProjectName})"
    End Function
End Class