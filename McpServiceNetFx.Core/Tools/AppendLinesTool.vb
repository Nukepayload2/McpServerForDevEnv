Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports McpServiceNetFx.Core.Models

''' <summary>
''' 追加内容到文件末尾的工具
''' </summary>
Public Class AppendLinesTool
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
        .Name = "append_lines",
        .Description = "追加内容到文件末尾，文件不存在时自动创建",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition) From {
                {
                    "filePath",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "要追加的文件路径"
                    }
                },
                {
                    "content",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "要追加的内容"
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
            .Required = {"filePath", "content"}
        }
    }

    ''' <summary>
    ''' 获取默认权限级别
    ''' </summary>
    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel
        Get
            Return PermissionLevel.Ask ' 追加操作需要确认
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
            ValidateRequiredArguments(arguments, "filePath", "content")

            ' 获取参数
            Dim filePath = CStr(arguments("filePath"))
            Dim content = CStr(arguments("content"))
            Dim encoding = GetOptionalArgument(arguments, "encoding", "UTF-8")

            ' 编码验证（目前只支持UTF-8）
            If Not String.Equals(encoding, "UTF-8", StringComparison.OrdinalIgnoreCase) Then
                Throw New McpException($"当前仅支持UTF-8编码，请求的编码: {encoding}", McpErrorCode.InvalidParams)
            End If

            LogOperation("追加文件", "开始", $"文件: {filePath}, 大小: {content.Length} 字符")

            ' 调用文件操作辅助类
            Dim appendResult = FileOperationHelper.AppendToFileSafely(filePath, content)

            ' 获取文件总行数
            Dim totalLines As Integer = 0
            If System.IO.File.Exists(filePath) Then
                Try
                    totalLines = System.IO.File.ReadAllLines(filePath, System.Text.Encoding.UTF8).Length
                Catch
                    ' 如果无法读取行数，保持为0
                End Try
            End If

            ' 转换为工具结果
            Dim toolResult As New AppendLinesResult With {
                .Success = appendResult.Success,
                .LinesAppended = appendResult.LinesWritten,
                .NewHash = appendResult.NewHash,
                .TotalLines = totalLines
            }

            If appendResult.Success Then
                toolResult.Message = $"成功追加 {toolResult.LinesAppended} 行，文件总行数: {toolResult.TotalLines}"
                LogOperation("追加文件", "完成", toolResult.Message)
            Else
                toolResult.Message = appendResult.Error
                toolResult.ErrorCode = appendResult.ErrorCode
                toolResult.Details = $"文件: {filePath}, 大小: {content.Length} 字符"
                LogOperation("追加文件", "失败", appendResult.Error)

                ' 如果是业务错误，返回成功状态但包含错误信息
                If appendResult.ErrorCode = "INVALID_PATH" OrElse
                   appendResult.ErrorCode = "SIZE_EXCEEDED" Then
                    toolResult.Success = True ' 工具执行成功，但业务操作失败
                Else
                    Throw New McpException(appendResult.Error, McpErrorCode.InternalError)
                End If
            End If

            Return toolResult

        Catch ex As McpException
            LogOperation("追加文件", "失败", ex.Message)
            Throw
        Catch ex As Exception
            LogOperation("追加文件", "失败", ex.Message)
            Throw New McpException($"追加文件时发生错误: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function
End Class