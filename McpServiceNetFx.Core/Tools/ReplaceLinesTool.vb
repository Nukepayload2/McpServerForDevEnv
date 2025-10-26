Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports McpServiceNetFx.Core.Models

''' <summary>
''' 替换文件中指定行范围内容的工具
''' </summary>
Public Class ReplaceLinesTool
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
        .Name = "replace_lines",
        .Description = "替换文件中指定行范围的内容，配合带行号的ReadLines使用",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition) From {
                {
                    "filePath",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "要操作的文件路径"
                    }
                },
                {
                    "hash",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "文件当前HASH值（必需，用于校验）"
                    }
                },
                {
                    "start",
                    New PropertyDefinition With {
                        .Type = "number",
                        .Description = "起始行号（从1开始）"
                    }
                },
                {
                    "length",
                    New PropertyDefinition With {
                        .Type = "number",
                        .Description = "要替换的行数"
                    }
                },
                {
                    "content",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "新内容"
                    }
                },
                {
                    "encoding",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "文件编码格式，默认为UTF-8",
                        .[Default] = "UTF-8"
                    }
                }
            },
            .Required = {"filePath", "hash", "start", "length", "content"}
        }
    }

    ''' <summary>
    ''' 获取默认权限级别
    ''' </summary>
    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel
        Get
            Return PermissionLevel.Ask ' 替换操作需要确认
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
            ValidateRequiredArguments(arguments, "filePath", "hash", "start", "length", "content")

            ' 获取参数
            Dim filePath = CStr(arguments("filePath"))
            Dim expectedHash = CStr(arguments("hash"))
            Dim start = CInt(arguments("start"))
            Dim length = CInt(arguments("length"))
            Dim content = CStr(arguments("content"))
            Dim encoding = GetOptionalArgument(arguments, "encoding", "UTF-8")

            ' 编码验证（目前只支持UTF-8）
            If Not String.Equals(encoding, "UTF-8", StringComparison.OrdinalIgnoreCase) Then
                Throw New McpException($"当前仅支持UTF-8编码，请求的编码: {encoding}", McpErrorCode.InvalidParams)
            End If

            ' 参数验证
            If start < 0 Then
                Throw New McpException("起始行号不能为负数", McpErrorCode.InvalidParams)
            End If

            If length <= 0 Then
                Throw New McpException("替换行数必须大于0", McpErrorCode.InvalidParams)
            End If

            LogOperation("替换文件行", "开始", $"文件: {filePath}, 起始行: {start}, 行数: {length}")

            ' 调用文件操作辅助类
            Dim replaceResult = FileOperationHelper.ReplaceLinesInFile(filePath, start - 1, length, content, expectedHash)

            ' 计算新内容的行数
            Dim newLinesCount = 0
            If Not String.IsNullOrEmpty(content) Then
                newLinesCount = content.Split({vbCrLf, vbLf}, StringSplitOptions.None).Length
            End If

            ' 获取文件总行数
            Dim totalLines As Integer = 0
            If System.IO.File.Exists(filePath) AndAlso replaceResult.Success Then
                Try
                    totalLines = System.IO.File.ReadAllLines(filePath, System.Text.Encoding.UTF8).Length
                Catch
                    ' 如果无法读取行数，保持为0
                End Try
            End If

            ' 转换为工具结果
            Dim toolResult As New ReplaceLinesResult With {
                .Success = replaceResult.Success,
                .LinesReplaced = replaceResult.ReplacementsCount,
                .LinesAdded = newLinesCount,
                .NewHash = replaceResult.NewHash,
                .TotalLines = totalLines
            }

            If replaceResult.Success Then
                toolResult.Message = $"成功替换 {toolResult.LinesReplaced} 行为 {toolResult.LinesAdded} 行，文件总行数: {toolResult.TotalLines}"
                LogOperation("替换文件行", "完成", toolResult.Message)
            Else
                toolResult.Message = replaceResult.Error
                toolResult.ErrorCode = replaceResult.ErrorCode
                toolResult.Details = $"文件: {filePath}, 起始行: {start}, 行数: {length}"
                LogOperation("替换文件行", "失败", replaceResult.Error)

                ' 如果是业务错误，返回成功状态但包含错误信息
                If replaceResult.ErrorCode = "HASH_MISMATCH" OrElse
                   replaceResult.ErrorCode = "FILE_NOT_FOUND" OrElse
                   replaceResult.ErrorCode = "INVALID_PATH" OrElse
                   replaceResult.ErrorCode = "INVALID_START_LINE" OrElse
                   replaceResult.ErrorCode = "INVALID_LENGTH" OrElse
                   replaceResult.ErrorCode = "START_BEYOND_EOF" Then
                    toolResult.Success = True ' 工具执行成功，但业务操作失败
                Else
                    Throw New McpException(replaceResult.Error, McpErrorCode.InternalError)
                End If
            End If

            Return toolResult

        Catch ex As McpException
            LogOperation("替换文件行", "失败", ex.Message)
            Throw
        Catch ex As Exception
            LogOperation("替换文件行", "失败", ex.Message)
            Throw New McpException($"替换文件行时发生错误: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function
End Class