Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports McpServiceNetFx.Core.Models

''' <summary>
''' 字符串替换工具（支持普通替换和正则表达式替换）
''' </summary>
Public Class StringReplaceTool
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
        .Name = "string_replace",
        .Description = "在文件中进行字符串替换，支持普通替换和正则表达式替换（ECMAScript模式）",
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
                    "oldText",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "要替换的文本"
                    }
                },
                {
                    "newText",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "新文本"
                    }
                },
                {
                    "useRegex",
                    New PropertyDefinition With {
                        .Type = "boolean",
                        .Description = "是否使用正则表达式，默认为false",
                        .[Default] = "false"
                    }
                },
                {
                    "ignoreCase",
                    New PropertyDefinition With {
                        .Type = "boolean",
                        .Description = "是否忽略大小写，默认为false",
                        .[Default] = "false"
                    }
                },
                {
                    "multiline",
                    New PropertyDefinition With {
                        .Type = "boolean",
                        .Description = "正则表达式多行模式（^和$匹配行的开始和结束），默认为false",
                        .[Default] = "false"
                    }
                },
                {
                    "singleline",
                    New PropertyDefinition With {
                        .Type = "boolean",
                        .Description = "正则表达式单行模式（.匹配包括换行符在内的所有字符），默认为false",
                        .[Default] = "false"
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
            .Required = {"filePath", "hash", "oldText", "newText"}
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
            ValidateRequiredArguments(arguments, "filePath", "hash", "oldText", "newText")

            ' 获取参数
            Dim filePath = CStr(arguments("filePath"))
            Dim expectedHash = CStr(arguments("hash"))
            Dim oldText = CStr(arguments("oldText"))
            Dim newText = CStr(arguments("newText"))
            Dim useRegex = GetOptionalArgument(arguments, "useRegex", False)
            Dim ignoreCase = GetOptionalArgument(arguments, "ignoreCase", False)
            Dim multiline = GetOptionalArgument(arguments, "multiline", False)
            Dim singleline = GetOptionalArgument(arguments, "singleline", False)
            Dim encoding = GetOptionalArgument(arguments, "encoding", "UTF-8")

            ' 编码验证（目前只支持UTF-8）
            If Not String.Equals(encoding, "UTF-8", StringComparison.OrdinalIgnoreCase) Then
                Throw New McpException($"当前仅支持UTF-8编码，请求的编码: {encoding}", McpErrorCode.InvalidParams)
            End If

            ' 参数验证
            If String.IsNullOrEmpty(oldText) Then
                Throw New McpException("要替换的文本不能为空", McpErrorCode.InvalidParams)
            End If

            ' 构建替换选项
            Dim options As New StringReplaceOptions With {
                .UseRegex = useRegex,
                .IgnoreCase = ignoreCase,
                .Multiline = multiline,
                .Singleline = singleline
            }

            Dim operationType = If(useRegex, "正则表达式替换", "字符串替换")
            LogOperation("替换文件内容", "开始", $"文件: {filePath}, 操作: {operationType}, 忽略大小写: {ignoreCase}")

            ' 调用文件操作辅助类
            Dim replaceResult = FileOperationHelper.ReplaceInFile(filePath, oldText, newText, options, expectedHash)

            ' 转换为工具结果
            Dim toolResult As New StringReplaceResult With {
                .Success = replaceResult.Success,
                .ReplacementsCount = replaceResult.ReplacementsCount,
                .NewHash = replaceResult.NewHash,
                .UsedRegex = useRegex
            }

            If replaceResult.Success Then
                If replaceResult.ReplacementsCount > 0 Then
                    toolResult.Message = $"成功替换 {toolResult.ReplacementsCount} 处"
                Else
                    toolResult.Message = "没有找到匹配的文本"
                    toolResult.ErrorCode = "NO_MATCHES"
                End If
                LogOperation("替换文件内容", "完成", toolResult.Message)
            Else
                toolResult.Message = replaceResult.Error
                toolResult.ErrorCode = replaceResult.ErrorCode
                toolResult.Details = $"文件: {filePath}, 操作: {operationType}, 原文本: {oldText}"
                LogOperation("替换文件内容", "失败", replaceResult.Error)

                ' 如果是业务错误，返回成功状态但包含错误信息
                If replaceResult.ErrorCode = "HASH_MISMATCH" OrElse
                   replaceResult.ErrorCode = "FILE_NOT_FOUND" OrElse
                   replaceResult.ErrorCode = "INVALID_PATH" OrElse
                   replaceResult.ErrorCode = "INVALID_OLD_TEXT" OrElse
                   replaceResult.ErrorCode = "REGEX_ERROR" OrElse
                   replaceResult.ErrorCode = "NO_MATCHES" Then
                    toolResult.Success = True ' 工具执行成功，但业务操作失败
                Else
                    Throw New McpException(replaceResult.Error, McpErrorCode.InternalError)
                End If
            End If

            Return toolResult

        Catch ex As McpException
            LogOperation("替换文件内容", "失败", ex.Message)
            Throw
        Catch ex As Exception
            LogOperation("替换文件内容", "失败", ex.Message)
            Throw New McpException($"替换文件内容时发生错误: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function
End Class