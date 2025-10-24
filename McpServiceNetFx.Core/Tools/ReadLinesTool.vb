Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports McpServiceNetFx.Core.Models

''' <summary>
''' 读取文件指定行范围内容的工具
''' </summary>
Public Class ReadLinesTool
    Inherits VisualStudioToolBase

    ''' <summary>
    ''' 创建工具实例
    ''' </summary>
    ''' <param name="logger">日志记录器</param>
    ''' <param name="permissionHandler">权限处理器</param>
    Public Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, permissionHandler)
    End Sub

    ''' <summary>
    ''' 获取工具定义
    ''' </summary>
    Public Overrides ReadOnly Property ToolDefinition As New ToolDefinition With {
        .Name = "read_lines",
        .Description = "读取文件指定行范围的内容，最多100行，支持返回带行号或不带行号的格式",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition) From {
                {
                    "filePath",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "要读取的文件路径"
                    }
                },
                {
                    "start",
                    New PropertyDefinition With {
                        .Type = "number",
                        .Description = "起始行号（从0开始），默认为0",
                        .[Default] = "0"
                    }
                },
                {
                    "length",
                    New PropertyDefinition With {
                        .Type = "number",
                        .Description = "读取的行数（1-100），默认为100",
                        .[Default] = "100"
                    }
                },
                {
                    "withLineNumbers",
                    New PropertyDefinition With {
                        .Type = "boolean",
                        .Description = "是否返回带行号的二维数组，默认为false",
                        .[Default] = "false"
                    }
                }
            },
            .Required = {"filePath"}
        }
    }

    ''' <summary>
    ''' 获取默认权限级别
    ''' </summary>
    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel
        Get
            Return PermissionLevel.Allow
        End Get
    End Property

    ''' <summary>
    ''' 执行工具的具体实现
    ''' </summary>
    ''' <param name="arguments">工具参数</param>
    ''' <returns>执行结果</returns>
    Protected Overrides Async Function ExecuteInternalAsync(arguments As Dictionary(Of String, Object)) As Task(Of Object)
        Try
            ' 检查权限
            If Not CheckPermission() Then
                Throw New McpException("权限被拒绝", McpErrorCode.InvalidParams)
            End If

            ' 验证必需参数
            ValidateRequiredArguments(arguments, "filePath")

            ' 获取参数
            Dim filePath = CStr(arguments("filePath"))
            Dim start = GetOptionalArgument(arguments, "start", 0)
            Dim length = GetOptionalArgument(arguments, "length", 100)
            Dim withLineNumbers = GetOptionalArgument(arguments, "withLineNumbers", False)

            ' 参数验证
            If start < 0 Then
                Throw New McpException("起始行号不能为负数", McpErrorCode.InvalidParams)
            End If

            If length <= 0 OrElse length > FileOperationHelper.MAX_READ_LINES Then
                Throw New McpException($"读取行数必须在1到{FileOperationHelper.MAX_READ_LINES}之间", McpErrorCode.InvalidParams)
            End If

            LogOperation("读取文件行", "开始", $"文件: {filePath}, 起始: {start}, 行数: {length}")

            ' 调用文件操作辅助类
            Dim readResult = FileOperationHelper.ReadFileLinesSafely(filePath, start, length)

            ' 转换为工具结果
            Dim toolResult As New ReadLinesResult With {
                .Success = readResult.Success,
                .TotalLines = readResult.TotalLines,
                .Hash = readResult.Hash,
                .LinesRead = If(readResult.Lines?.Length, 0)
            }

            If readResult.Success Then
                ' 根据withLineNumbers参数决定返回格式
                If withLineNumbers Then
                    ' 返回带行号的二维数组 [[1, "line1"], [2, "line2"], ...]
                    Dim contentWithLineNumbers(readResult.Lines.Length - 1)() As Object
                    For i = 0 To readResult.Lines.Length - 1
                        contentWithLineNumbers(i) = {start + i + 1, readResult.Lines(i)}
                    Next
                    toolResult.Content = contentWithLineNumbers
                Else
                    ' 返回普通字符串数组
                    toolResult.Content = readResult.Lines
                End If

                toolResult.Message = $"成功读取 {toolResult.LinesRead} 行，文件总行数: {toolResult.TotalLines}"
                LogOperation("读取文件行", "完成", toolResult.Message)
            Else
                toolResult.Message = readResult.Error
                toolResult.ErrorCode = readResult.ErrorCode
                toolResult.Details = $"文件: {filePath}, 起始行: {start}, 读取行数: {length}"
                LogOperation("读取文件行", "失败", readResult.Error)

                ' 如果是业务错误（如文件不存在），返回成功状态但包含错误信息
                If readResult.ErrorCode = "FILE_NOT_FOUND" OrElse
                   readResult.ErrorCode = "START_BEYOND_EOF" OrElse
                   readResult.ErrorCode = "INVALID_PATH" Then
                    toolResult.Success = True ' 工具执行成功，但业务操作失败
                Else
                    Throw New McpException(readResult.Error, McpErrorCode.InternalError)
                End If
            End If

            Return toolResult

        Catch ex As McpException
            LogOperation("读取文件行", "失败", ex.Message)
            Throw
        Catch ex As Exception
            LogOperation("读取文件行", "失败", ex.Message)
            Throw New McpException($"读取文件行时发生错误: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function
End Class