''' <summary>
''' 获取当前错误和警告列表的工具
''' </summary>
Public Class GetErrorListTool
    Inherits VisualStudioToolBase

    ''' <summary>
    ''' 创建工具实例
    ''' </summary>
    Public Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, permissionHandler)
    End Sub

    Public Overrides ReadOnly Property ToolDefinition As New ToolDefinition With {
        .Name = "get_error_list",
        .Description = "获取当前的错误和警告列表",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition) From {
                {"severity", New PropertyDefinition With {
                    .Type = "string",
                    .Description = "过滤级别 (Error/Warning/Message/All)",
                    .[Default] = "All"
                }}
            }
        }
    }

    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel
        Get
            Return PermissionLevel.Allow
        End Get
    End Property

    Protected Overrides Async Function ExecuteInternalAsync(arguments As Dictionary(Of String, Object)) As Task(Of Object)
        Try
            ' 检查权限
            If Not CheckPermission() Then
                Throw New McpException("权限被拒绝", McpErrorCode.InvalidParams)
            End If

            ' 获取过滤级别参数
            Dim severity = GetOptionalArgument(Of String)(arguments, "severity", "All")

            LogOperation("获取错误列表", "开始", $"过滤级别: {severity}")

            ' 使用异步方法
            Dim result = Await _vsTools.GetErrorListAsync(severity)

            LogOperation("获取错误列表", "完成", $"错误数: {result.Errors.Count}, 警告数: {result.Warnings.Count}")

            ' 转换为强类型响应
            Return New ErrorListResponse With {
                .Errors = If(result.Errors?.ToArray(), New CompilationError() {}),
                .Warnings = If(result.Warnings?.ToArray(), New CompilationError() {}),
                .TotalCount = result.Errors.Count + result.Warnings.Count
            }

        Catch ex As Exception
            LogOperation("获取错误列表", "失败", ex.Message)
            Throw New McpException($"获取错误列表失败: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function
End Class