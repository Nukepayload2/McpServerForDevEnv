Option Compare Text
Imports System.Collections.Concurrent
Imports System.IO
Imports System.Text.RegularExpressions

''' <summary>
''' 按文本内容查找文件的工具
''' </summary>
Public Class FindFilesByTextContentTool
    Inherits VisualStudioToolBase

    Private ReadOnly _toolDefinition As Lazy(Of ToolDefinition)

    ''' <summary>
    ''' 创建工具实例
    ''' </summary>
    Public Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, permissionHandler)
        _toolDefinition = New Lazy(Of ToolDefinition)(AddressOf CreateToolDefinition)
    End Sub

    ''' <summary>
    ''' 获取工具定义
    ''' </summary>
    Public Overrides ReadOnly Property ToolDefinition As ToolDefinition
        Get
            Return _toolDefinition.Value
        End Get
    End Property

    ''' <summary>
    ''' 创建工具定义
    ''' </summary>
    Private Function CreateToolDefinition() As ToolDefinition
        Return New ToolDefinition With {
            .Name = "find_files_by_text_content",
            .Description = "在指定文件中按文本内容查找，支持纯文本、通配符和正则表达式匹配模式",
            .InputSchema = New InputSchema With {
                .Type = "object",
                .Properties = New Dictionary(Of String, PropertyDefinition) From {
                    {
                        "findWhat",
                        New PropertyDefinition With {
                            .Type = "string",
                            .Description = "查找内容"
                        }
                    },
                    {
                        "lookIn",
                        New PropertyDefinition With {
                            .Type = "array",
                            .Description = "要搜索的文件路径列表"
                        }
                    },
                    {
                        "matchMode",
                        New PropertyDefinition With {
                            .Type = "string",
                            .Description = "匹配模式：plainText（纯文本）、wildcard（通配符）或 regex（正则表达式）",
                            .[Default] = "plainText"
                        }
                    },
                    {
                        "encoding",
                        New PropertyDefinition With {
                            .Type = "string",
                            .Description = "文件编码：utf8 或 ansi",
                            .[Default] = "utf8"
                        }
                    }
                },
                .Required = {"findWhat", "lookIn"}
            }
        }
    End Function

    ''' <summary>
    ''' 获取默认权限级别
    ''' </summary>
    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel
        Get
            Return PermissionLevel.Allow
        End Get
    End Property

    ''' <summary>
    ''' 标识为文件操作工具
    ''' </summary>
    Public Overrides ReadOnly Property IsFileTool As Boolean
        Get
            Return True
        End Get
    End Property

    ''' <summary>
    ''' 执行工具
    ''' </summary>
    Protected Overrides Async Function ExecuteInternalAsync(arguments As Dictionary(Of String, Object)) As Task(Of Object)
        Try
            ' 验证必需参数
            ValidateRequiredArguments(arguments, "findWhat", "lookIn")

            ' 获取参数
            Dim findWhat = CStr(arguments("findWhat"))
            Dim lookInObj = arguments("lookIn")
            Dim matchModeStr = GetOptionalArgument(arguments, "matchMode", "plainText")
            Dim encodingStr = GetOptionalArgument(arguments, "encoding", "utf8")

            ' 解析文件列表
            Dim files As String() = ParseFileList(lookInObj)
            If files Is Nothing OrElse files.Length = 0 Then
                Return New FindFilesByTextContentResult With {
                    .Success = True,
                    .Message = "没有提供要搜索的文件",
                    .matches = {},
                    .FilesSearched = 0,
                    .FilesMatched = 0
                }
            End If

            ' 解析枚举
            Dim matchMode As TextMatchMode
            If Not [Enum].TryParse(matchModeStr, True, matchMode) Then
                matchMode = TextMatchMode.plainText
            End If

            Dim encoding As FileEncoding
            If Not [Enum].TryParse(encodingStr, True, encoding) Then
                encoding = FileEncoding.utf8
            End If

            LogOperation("按文本内容查找", "开始", $"模式: {matchMode}, 文件数: {files.Length}, 查找内容长度: {findWhat.Length}")

            ' 获取解决方案目录用于权限检查
            Dim solutionInfo = Await _vsTools.GetSolutionInformationAsync()
            Dim solutionDir As String = Nothing
            If solutionInfo IsNot Nothing AndAlso Not String.IsNullOrEmpty(solutionInfo.FullName) Then
                solutionDir = Path.GetDirectoryName(solutionInfo.FullName)
            End If

            ' 检查权限并过滤文件
            Dim filesToSearch As New List(Of String)()
            For Each filePath In files
                Dim needPermissionCheck As Boolean = False
                If solutionDir IsNot Nothing Then
                    needPermissionCheck = Not filePath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase)
                Else
                    needPermissionCheck = True
                End If

                If needPermissionCheck Then
                    If CheckFilePermission(filePath, FileAccessType.Read) Then
                        filesToSearch.Add(filePath)
                    End If
                Else
                    filesToSearch.Add(filePath)
                End If
            Next

            If filesToSearch.Count = 0 Then
                Return New FindFilesByTextContentResult With {
                    .Success = True,
                    .Message = "没有可搜索的文件（可能由于权限限制）",
                    .matches = {},
                    .FilesSearched = 0,
                    .FilesMatched = 0
                }
            End If

            ' 准备匹配器
            Dim matcher As ITextMatcher = CreateMatcher(matchMode, findWhat)

            ' 多线程处理：Task.Run 在外层，Parallel.ForEach 在内层
            Dim matches = Await Task.Run(Function()
                                             Dim bag As New ConcurrentBag(Of FileContentMatch)()
                                             Dim encodingToUse As System.Text.Encoding = If(encoding = FileEncoding.utf8, System.Text.Encoding.UTF8, System.Text.Encoding.Default)

                                             Parallel.ForEach(filesToSearch, Sub(filePath)
                                                                                 Try
                                                                                     If Not File.Exists(filePath) Then
                                                                                         Exit Sub
                                                                                     End If

                                                                                     Dim result = ProcessFile(filePath, matcher, encodingToUse)
                                                                                     If result IsNot Nothing Then
                                                                                         bag.Add(result)
                                                                                     End If
                                                                                 Catch ex As Exception
                                                                                     ' 忽略单个文件的处理错误，继续处理其他文件
                                                                                 End Try
                                                                             End Sub)

                                             Return bag.ToArray()
                                         End Function)

            LogOperation("按文本内容查找", "完成", $"搜索 {filesToSearch.Count} 个文件，找到 {matches.Count(Function(m) m IsNot Nothing)} 个匹配")

            Return New FindFilesByTextContentResult With {
                .Success = True,
                .Message = $"搜索了 {filesToSearch.Count} 个文件，在 {matches.Count(Function(m) m IsNot Nothing)} 个文件中找到匹配",
                .matches = matches.Where(Function(m) m IsNot Nothing).ToArray(),
                .FilesSearched = filesToSearch.Count,
                .FilesMatched = matches.Count(Function(m) m IsNot Nothing)
            }

        Catch ex As McpException
            LogOperation("按文本内容查找", "失败", ex.Message)
            Throw
        Catch ex As Exception
            LogOperation("按文本内容查找", "失败", ex.Message)
            Throw New McpException($"按文本内容查找时发生错误: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function

    ''' <summary>
    ''' 解析文件列表参数
    ''' </summary>
    Private Function ParseFileList(lookInObj As Object) As String()
        If lookInObj Is Nothing Then
            Return {}
        End If

        ' 处理数组类型
        If TypeOf lookInObj Is Object() Then
            Return DirectCast(lookInObj, Object()).Select(Function(o) CStr(o)).ToArray()
        ElseIf TypeOf lookInObj Is String() Then
            Return DirectCast(lookInObj, String())
        ElseIf TypeOf lookInObj Is List(Of String) Then
            Return DirectCast(lookInObj, List(Of String)).ToArray()
        ElseIf TypeOf lookInObj Is String Then
            ' 单个文件路径作为字符串
            Return {CStr(lookInObj)}
        End If

        ' 尝试作为 Newtonsoft.Json 数组处理
        If lookInObj.GetType().IsArray Then
            Return DirectCast(lookInObj, Object()).Select(Function(o) CStr(o)).ToArray()
        End If

        Return {}
    End Function

    ''' <summary>
    ''' 创建文本匹配器
    ''' </summary>
    Private Function CreateMatcher(matchMode As TextMatchMode, findWhat As String) As ITextMatcher
        Select Case matchMode
            Case TextMatchMode.plainText
                Return New PlainTextMatcher(findWhat)
            Case TextMatchMode.wildcard
                Return New WildcardMatcher(findWhat)
            Case TextMatchMode.regex
                Return New RegexMatcher(findWhat)
            Case Else
                Return New PlainTextMatcher(findWhat)
        End Select
    End Function

    ''' <summary>
    ''' 处理单个文件
    ''' </summary>
    Private Function ProcessFile(filePath As String, matcher As ITextMatcher, encoding As System.Text.Encoding) As FileContentMatch
        Try
            Dim lines = File.ReadAllLines(filePath, encoding)
            Dim lineNumbers As New List(Of Integer)()
            Dim matchCount As Integer = 0

            For i = 0 To lines.Length - 1
                If matcher.IsMatch(lines(i)) Then
                    lineNumbers.Add(i + 1) ' 1-based 行号
                    matchCount += 1
                End If
            Next

            If matchCount > 0 Then
                Return New FileContentMatch With {
                    .filePath = filePath,
                    .lineNumbers = lineNumbers.ToArray(),
                    .matchCount = matchCount
                }
            End If

            Return Nothing
        Catch ex As Exception
            ' 忽略文件读取错误
            Return Nothing
        End Try
    End Function

End Class

''' <summary>
''' 文本匹配器接口
''' </summary>
Public Interface ITextMatcher
    Function IsMatch(text As String) As Boolean
End Interface

''' <summary>
''' 纯文本匹配器（忽略大小写）
''' </summary>
Public Class PlainTextMatcher
    Implements ITextMatcher

    Private ReadOnly _findWhat As String

    Public Sub New(findWhat As String)
        _findWhat = findWhat
    End Sub

    Public Function IsMatch(text As String) As Boolean Implements ITextMatcher.IsMatch
        Return text.IndexOf(_findWhat, StringComparison.OrdinalIgnoreCase) >= 0
    End Function
End Class

''' <summary>
''' 通配符匹配器（使用 VB Like 运算符，Option Compare Text 模式）
''' </summary>
Public Class WildcardMatcher
    Implements ITextMatcher

    Private ReadOnly _findWhat As String

    Public Sub New(findWhat As String)
        _findWhat = findWhat
    End Sub

    Public Function IsMatch(text As String) As Boolean Implements ITextMatcher.IsMatch
        ' 使用 VB Like 运算符，Option Compare Text 模式（忽略大小写）
        Return text Like _findWhat
    End Function
End Class

''' <summary>
''' 正则表达式匹配器（忽略大小写、预编译、ECMAScript 模式）
''' </summary>
Public Class RegexMatcher
    Implements ITextMatcher

    Private ReadOnly _regex As Regex

    Public Sub New(pattern As String)
        Dim options As RegexOptions = RegexOptions.IgnoreCase Or RegexOptions.Compiled Or RegexOptions.ECMAScript
        _regex = New Regex(pattern, options)
    End Sub

    Public Function IsMatch(text As String) As Boolean Implements ITextMatcher.IsMatch
        Return _regex.IsMatch(text)
    End Function
End Class
