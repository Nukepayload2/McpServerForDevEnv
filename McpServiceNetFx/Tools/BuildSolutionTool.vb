''' <summary>
''' 构建整个解决方案的工具
''' </summary>
Public Class BuildSolutionTool
    Inherits VisualStudioToolBase

    ''' <summary>
    ''' 创建工具实例
    ''' </summary>
    Public Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, permissionHandler)
    End Sub

    Public Overrides ReadOnly Property ToolDefinition As New ToolDefinition With {
        .Name = "build_solution",
        .Description = "构建整个解决方案",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition) From {
                {"configuration", New PropertyDefinition With {
                    .Type = "string",
                    .Description = "构建配置 (Debug/Release)",
                    .[Default] = "Debug"
                }}
            }
        }
    }

    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel
        Get
            Return PermissionLevel.Ask
        End Get
    End Property

    Protected Overrides Async Function ExecuteInternalAsync(arguments As Dictionary(Of String, Object)) As Task(Of Object)
        Try
            ' 检查权限
            If Not CheckPermission() Then
                Throw New McpException("权限被拒绝", McpErrorCode.InvalidParams)
            End If

            ' 获取配置参数
            Dim configuration = GetOptionalArgument(Of String)(arguments, "configuration", "Debug")

            LogOperation("构建解决方案", "开始", $"配置: {configuration}")

            Dim result = Await _vsTools.BuildSolutionAsync(configuration)

            LogOperation("构建解决方案", If(result.Success, "成功", "失败"), result.Message)

            ' 转换为强类型响应
            Return New BuildResultResponse With {
                .Success = result.Success,
                .Message = result.Message,
                .BuildTime = result.BuildTime,
                .Configuration = result.Configuration,
                .Errors = If(result.Errors?.ToArray(), New CompilationError() {}),
                .Warnings = If(result.Warnings?.ToArray(), New CompilationError() {})
            }

        Catch ex As Exception
            LogOperation("构建解决方案", "失败", ex.Message)
            Throw New McpException($"构建解决方案失败: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function
End Class