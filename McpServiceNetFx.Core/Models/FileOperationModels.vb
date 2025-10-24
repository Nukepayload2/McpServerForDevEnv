Imports Newtonsoft.Json
Imports System.Text

''' <summary>
''' 文件操作工具的数据模型
''' </summary>

''' <summary>
''' 文件操作结果基类
''' </summary>
Public MustInherit Class FileOperationResultBase
    ''' <summary>
    ''' 操作是否成功
    ''' </summary>
    <JsonProperty("success")>
    Public Property Success As Boolean

    ''' <summary>
    ''' 操作结果消息
    ''' </summary>
    <JsonProperty("message")>
    Public Property Message As String

    ''' <summary>
    ''' 错误代码
    ''' </summary>
    <JsonProperty("errorCode")>
    Public Property ErrorCode As String

    ''' <summary>
    ''' 详细信息
    ''' </summary>
    <JsonProperty("details")>
    Public Property Details As String
End Class

''' <summary>
''' ReadLines 工具的结果模型
''' </summary>
Public Class ReadLinesResult
    Inherits FileOperationResultBase

    ''' <summary>
    ''' 文件总行数
    ''' </summary>
    <JsonProperty("totalLines")>
    Public Property TotalLines As Integer

    ''' <summary>
    ''' 文件内容的HASH值
    ''' </summary>
    <JsonProperty("hash")>
    Public Property [Hash] As String

    ''' <summary>
    ''' 文件内容（字符串数组或带行号的二维数组）
    ''' </summary>
    <JsonProperty("content")>
    Public Property Content As Object

    ''' <summary>
    ''' 实际读取的行数
    ''' </summary>
    <JsonProperty("linesRead")>
    Public Property LinesRead As Integer
End Class

''' <summary>
''' WriteLines 工具的结果模型
''' </summary>
Public Class WriteLinesResult
    Inherits FileOperationResultBase

    ''' <summary>
    ''' 写入的行数
    ''' </summary>
    <JsonProperty("linesWritten")>
    Public Property LinesWritten As Integer

    ''' <summary>
    ''' 写入后文件的新HASH值
    ''' </summary>
    <JsonProperty("newHash")>
    Public Property NewHash As String

    ''' <summary>
    ''' 写入的字节数
    ''' </summary>
    <JsonProperty("bytesWritten")>
    Public Property BytesWritten As Long
End Class

''' <summary>
''' AppendLines 工具的结果模型
''' </summary>
Public Class AppendLinesResult
    Inherits FileOperationResultBase

    ''' <summary>
    ''' 追加的行数
    ''' </summary>
    <JsonProperty("linesAppended")>
    Public Property LinesAppended As Integer

    ''' <summary>
    ''' 追加后文件的新HASH值
    ''' </summary>
    <JsonProperty("newHash")>
    Public Property NewHash As String

    ''' <summary>
    ''' 追加后文件的总行数
    ''' </summary>
    <JsonProperty("totalLines")>
    Public Property TotalLines As Integer
End Class

''' <summary>
''' StringReplace 工具的结果模型
''' </summary>
Public Class StringReplaceResult
    Inherits FileOperationResultBase

    ''' <summary>
    ''' 替换的次数
    ''' </summary>
    <JsonProperty("replacementsCount")>
    Public Property ReplacementsCount As Integer

    ''' <summary>
    ''' 替换后文件的新HASH值
    ''' </summary>
    <JsonProperty("newHash")>
    Public Property NewHash As String

    ''' <summary>
    ''' 是否使用了正则表达式
    ''' </summary>
    <JsonProperty("usedRegex")>
    Public Property UsedRegex As Boolean
End Class

''' <summary>
''' ReplaceLines 工具的结果模型
''' </summary>
Public Class ReplaceLinesResult
    Inherits FileOperationResultBase

    ''' <summary>
    ''' 替换的行数
    ''' </summary>
    <JsonProperty("linesReplaced")>
    Public Property LinesReplaced As Integer

    ''' <summary>
    ''' 新增的行数
    ''' </summary>
    <JsonProperty("linesAdded")>
    Public Property LinesAdded As Integer

    ''' <summary>
    ''' 替换后文件的新HASH值
    ''' </summary>
    <JsonProperty("newHash")>
    Public Property NewHash As String

    ''' <summary>
    ''' 替换后文件的总行数
    ''' </summary>
    <JsonProperty("totalLines")>
    Public Property TotalLines As Integer
End Class

''' <summary>
''' 字符串替换选项
''' </summary>
Public Structure StringReplaceOptions
    ''' <summary>
    ''' 是否使用正则表达式
    ''' </summary>
    Public Property UseRegex As Boolean

    ''' <summary>
    ''' 是否忽略大小写
    ''' </summary>
    Public Property IgnoreCase As Boolean

    ''' <summary>
    ''' 正则表达式多行模式
    ''' </summary>
    Public Property Multiline As Boolean

    ''' <summary>
    ''' 正则表达式单行模式
    ''' </summary>
    Public Property Singleline As Boolean

    ''' <summary>
    ''' 创建默认选项
    ''' </summary>
    Public Shared ReadOnly Property [Default] As StringReplaceOptions =
        New StringReplaceOptions With {
            .UseRegex = False,
            .IgnoreCase = False,
            .Multiline = False,
            .Singleline = False
        }
End Structure

''' <summary>
''' 文件读取操作的内部结果结构
''' </summary>
Public Structure FileReadResult
    ''' <summary>
    ''' 读取的行数组
    ''' </summary>
    Public Property Lines As String()

    ''' <summary>
    ''' 文件总行数
    ''' </summary>
    Public Property TotalLines As Integer

    ''' <summary>
    ''' 文件HASH值
    ''' </summary>
    Public Property Hash As String

    ''' <summary>
    ''' 操作是否成功
    ''' </summary>
    Public Property Success As Boolean

    ''' <summary>
    ''' 错误信息
    ''' </summary>
    Public Property [Error] As String

    ''' <summary>
    ''' 错误代码
    ''' </summary>
    Public Property ErrorCode As String
End Structure

''' <summary>
''' 文件写入操作的内部结果结构
''' </summary>
Public Structure FileWriteResult
    ''' <summary>
    ''' 写入的行数
    ''' </summary>
    Public Property LinesWritten As Integer

    ''' <summary>
    ''' 写入的字节数
    ''' </summary>
    Public Property BytesWritten As Long

    ''' <summary>
    ''' 新文件HASH值
    ''' </summary>
    Public Property NewHash As String

    ''' <summary>
    ''' 操作是否成功
    ''' </summary>
    Public Property Success As Boolean

    ''' <summary>
    ''' 错误信息
    ''' </summary>
    Public Property [Error] As String

    ''' <summary>
    ''' 错误代码
    ''' </summary>
    Public Property ErrorCode As String
End Structure

''' <summary>
''' 字符串替换操作的内部结果结构
''' </summary>
Public Structure FileReplaceResult
    ''' <summary>
    ''' 替换的次数
    ''' </summary>
    Public Property ReplacementsCount As Integer

    ''' <summary>
    ''' 新文件HASH值
    ''' </summary>
    Public Property NewHash As String

    ''' <summary>
    ''' 操作是否成功
    ''' </summary>
    Public Property Success As Boolean

    ''' <summary>
    ''' 错误信息
    ''' </summary>
    Public Property [Error] As String

    ''' <summary>
    ''' 错误代码
    ''' </summary>
    Public Property ErrorCode As String
End Structure