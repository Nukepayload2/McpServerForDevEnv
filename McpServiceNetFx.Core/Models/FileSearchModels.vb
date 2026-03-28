Imports Newtonsoft.Json

''' <summary>
''' 文件搜索工具的数据模型
''' </summary>

''' <summary>
''' 文件名匹配模式
''' </summary>
Public Enum FileNameMatchMode
    ''' <summary>
    ''' 通配符模式（使用 Directory.GetFiles 的通配符语法）
    ''' </summary>
    Wildcard

    ''' <summary>
    ''' 正则表达式模式
    ''' </summary>
    Regex
End Enum

''' <summary>
''' 搜索模式
''' </summary>
Public Enum SearchMode
    ''' <summary>
    ''' 递归搜索所有子目录
    ''' </summary>
    Recursive

    ''' <summary>
    ''' 仅搜索顶层目录
    ''' </summary>
    TopLevelOnly
End Enum

''' <summary>
''' 文本内容匹配模式
''' </summary>
Public Enum TextMatchMode
    ''' <summary>
    ''' 纯文本匹配（忽略大小写）
    ''' </summary>
    PlainText

    ''' <summary>
    ''' 通配符匹配（使用 VB Like 语法）
    ''' </summary>
    Wildcard

    ''' <summary>
    ''' 正则表达式匹配
    ''' </summary>
    Regex
End Enum

''' <summary>
''' 文件编码
''' </summary>
Public Enum FileEncoding
    ''' <summary>
    ''' UTF-8 编码
    ''' </summary>
    Utf8

    ''' <summary>
    ''' ANSI 编码（系统默认编码）
    ''' </summary>
    Ansi
End Enum

''' <summary>
''' FindFilesByName 工具的结果模型
''' </summary>
Public Class FindFilesByNameResult
    Inherits FileOperationResultBase

    ''' <summary>
    ''' 匹配的文件路径数组
    ''' </summary>
    <JsonProperty("files")>
    Public Property Files As String()

    ''' <summary>
    ''' 匹配的文件数量
    ''' </summary>
    <JsonProperty("count")>
    Public Property Count As Integer

    ''' <summary>
    ''' 搜索的目录
    ''' </summary>
    <JsonProperty("searchDirectory")>
    Public Property SearchDirectory As String

    ''' <summary>
    ''' 使用的匹配模式
    ''' </summary>
    <JsonProperty("matchMode")>
    Public Property MatchMode As String
End Class

''' <summary>
''' FindFilesByTextContent 工具的结果模型
''' </summary>
Public Class FindFilesByTextContentResult
    Inherits FileOperationResultBase

    ''' <summary>
    ''' 匹配结果列表
    ''' </summary>
    <JsonProperty("matches")>
    Public Property Matches As FileContentMatch()

    ''' <summary>
    ''' 搜索的文件总数
    ''' </summary>
    <JsonProperty("filesSearched")>
    Public Property FilesSearched As Integer

    ''' <summary>
    ''' 有匹配的文件数
    ''' </summary>
    <JsonProperty("filesMatched")>
    Public Property FilesMatched As Integer
End Class

''' <summary>
''' 文件内容匹配结果
''' </summary>
Public Class FileContentMatch
    ''' <summary>
    ''' 文件路径
    ''' </summary>
    <JsonProperty("filePath")>
    Public Property FilePath As String

    ''' <summary>
    ''' 匹配的行号列表（1-based）
    ''' </summary>
    <JsonProperty("lineNumbers")>
    Public Property LineNumbers As Integer()

    ''' <summary>
    ''' 匹配的次数
    ''' </summary>
    <JsonProperty("matchCount")>
    Public Property MatchCount As Integer
End Class
