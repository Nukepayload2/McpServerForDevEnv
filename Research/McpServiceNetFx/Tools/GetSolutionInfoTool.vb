''' <summary>
''' 获取当前解决方案信息的工具
''' </summary>
Public Class GetSolutionInfoTool
    Inherits VisualStudioToolBase

    Public Sub New(logger As IMcpLogger, vsTools As VisualStudioTools, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, vsTools, permissionHandler)
    End Sub

    Public Overrides ReadOnly Property ToolDefinition As New ToolDefinition With {
        .Name = "get_solution_info",
        .Description = "获取当前解决方案信息",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition)()
        }
    }

    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel
        Get
            Return PermissionLevel.Allow
        End Get
    End Property

    Public Overrides Async Function ExecuteAsync(arguments As Dictionary(Of String, Object)) As Task(Of Object)
        Try
            ' 检查权限
            If Not CheckPermission() Then
                Throw New McpException("权限被拒绝", McpErrorCode.InvalidParams)
            End If

            LogOperation("获取解决方案信息", "开始", "获取解决方案详细信息")

            ' 使用 Task.Run 确保异步执行
            Dim result = Await Task.Run(Function() _vsTools.GetSolutionInformation())

            LogOperation("获取解决方案信息", "完成", $"项目数: {result.Count}")

            Return result

        Catch ex As Exception
            LogOperation("获取解决方案信息", "失败", ex.Message)
            Throw New McpException($"获取解决方案信息失败: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function
End Class