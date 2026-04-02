Imports System.IO
Imports System.Text.RegularExpressions

''' <summary>
''' 按文件名查找文件的工具
''' </summary>
Public Class FindFilesByNameTool
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
            .Name = "find_files_by_name",
            .Description = "按文件名查找文件，支持通配符和正则表达式匹配模式",
            .InputSchema = New InputSchema With {
                .Type = "object",
                .Properties = New Dictionary(Of String, PropertyDefinition) From {
                    {
                        "findWhat",
                        New PropertyDefinition With {
                            .Type = "string",
                            .Description = "查找词/模式",
                            .[Default] = "*"
                        }
                    },
                    {
                        "lookIn",
                        New PropertyDefinition With {
                            .Type = "string",
                            .Description = "搜索目录（默认为解决方案目录）",
                            .[Default] = ""
                        }
                    },
                    {
                        "matchMode",
                        New PropertyDefinition With {
                            .Type = "string",
                            .Description = "匹配模式：wildcard（通配符）或 regex（正则表达式）",
                            .[Default] = "wildcard"
                        }
                    },
                    {
                        "searchMode",
                        New PropertyDefinition With {
                            .Type = "string",
                            .Description = "搜索模式：recursive（递归）或 topLevelOnly（仅顶层）",
                            .[Default] = "recursive"
                        }
                    }
                },
                .Required = {}
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
            ' 获取参数
            Dim findWhat = GetOptionalArgument(arguments, "findWhat", "*")
            Dim lookIn = GetOptionalArgument(arguments, "lookIn", "")
            Dim matchModeStr = GetOptionalArgument(arguments, "matchMode", "wildcard")
            Dim searchModeStr = GetOptionalArgument(arguments, "searchMode", "recursive")

            ' 解析枚举
            Dim matchMode As FileNameMatchMode
            If Not [Enum].TryParse(matchModeStr, True, matchMode) Then
                matchMode = FileNameMatchMode.Wildcard
            End If

            Dim searchMode As SearchMode
            If Not [Enum].TryParse(searchModeStr, True, searchMode) Then
                searchMode = SearchMode.Recursive
            End If

            ' 获取解决方案目录作为默认值
            Dim solutionInfo = Await _vsTools.GetSolutionInformationAsync()
            Dim solutionDir As String = Nothing
            If solutionInfo IsNot Nothing AndAlso Not String.IsNullOrEmpty(solutionInfo.FullName) Then
                Dim sloPath = solutionInfo.FullName
                If Not Directory.Exists(sloPath) Then
                    sloPath = Path.GetDirectoryName(sloPath)
                End If
                solutionDir = sloPath
            End If

            ' 确定搜索目录
            Dim searchDir As String
            If String.IsNullOrEmpty(lookIn) Then
                searchDir = solutionDir
            Else
                searchDir = Path.GetFullPath(lookIn)
            End If

            ' 验证搜索目录
            If String.IsNullOrEmpty(searchDir) OrElse Not Directory.Exists(searchDir) Then
                Return New FindFilesByNameResult With {
                    .Success = False,
                    .Message = "搜索目录不存在或未指定",
                    .ErrorCode = "INVALID_DIRECTORY",
                    .Files = {},
                    .Count = 0,
                    .SearchDirectory = searchDir,
                    .MatchMode = matchMode.ToString()
                }
            End If

            ' 权限检查：如果搜索目录在解决方案目录外，检查权限
            Dim needPermissionCheck As Boolean = False
            If solutionDir IsNot Nothing Then
                needPermissionCheck = Not searchDir.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase)
            Else
                needPermissionCheck = True
            End If

            If needPermissionCheck Then
                If Not CheckFilePermission(searchDir, FileAccessType.Read) Then
                    Throw New McpException("权限被拒绝：无法访问解决方案目录外的目录", McpErrorCode.InvalidParams)
                End If
            End If

            LogOperation("按文件名查找", "开始", $"目录: {searchDir}, 模式: {findWhat}, 匹配模式: {matchMode}, 搜索模式: {searchMode}")

            ' 确定搜索选项
            Dim searchOption As SearchOption = If(searchMode = SearchMode.Recursive, SearchOption.AllDirectories, SearchOption.TopDirectoryOnly)

            ' 执行搜索
            Dim files As String()
            If matchMode = FileNameMatchMode.Wildcard Then
                ' 通配符模式：直接使用 Directory.GetFiles
                files = Directory.GetFiles(searchDir, findWhat, searchOption)
            Else
                ' 正则模式：获取所有文件后用正则筛选
                Dim regexOptions As RegexOptions = RegexOptions.IgnoreCase Or RegexOptions.Compiled Or RegexOptions.ECMAScript
                Dim regex As New Regex(findWhat, regexOptions)
                Dim allFiles = Directory.GetFiles(searchDir, "*", searchOption)
                files = allFiles.Where(Function(f) regex.IsMatch(Path.GetFileName(f))).ToArray()
            End If

            LogOperation("按文件名查找", "完成", $"找到 {files.Length} 个文件")

            Return New FindFilesByNameResult With {
                .Success = True,
                .Message = $"找到 {files.Length} 个匹配的文件",
                .Files = files,
                .Count = files.Length,
                .SearchDirectory = searchDir,
                .MatchMode = matchMode.ToString()
            }

        Catch ex As McpException
            LogOperation("按文件名查找", "失败", ex.Message)
            Throw
        Catch ex As Exception
            LogOperation("按文件名查找", "失败", ex.Message)
            Throw New McpException($"按文件名查找时发生错误: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function
End Class
