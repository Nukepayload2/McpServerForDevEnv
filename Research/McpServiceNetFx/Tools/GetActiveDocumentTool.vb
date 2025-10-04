''' <summary>
''' 获取当前活动文档信息的工具
''' </summary>
Public Class GetActiveDocumentTool
    Inherits VisualStudioToolBase

    Public Sub New(logger As IMcpLogger, vsTools As VisualStudioTools, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, vsTools, permissionHandler)
    End Sub

    Public Overrides ReadOnly Property ToolDefinition As New ToolDefinition With {
        .Name = "get_active_document",
        .Description = "获取当前活动文档的路径",
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

            LogOperation("获取活动文档", "开始", "获取当前活动文档信息")

            ' 使用 Task.Run 确保异步执行
            Dim result = Await Task.Run(Function() _vsTools.GetActiveDocument())

            If result.HasActiveDocument Then
                LogOperation("获取活动文档", "完成", $"文档路径: {result.Path}")
            Else
                LogOperation("获取活动文档", "完成", "没有活动文档")
            End If

            Return result

        Catch ex As Exception
            LogOperation("获取活动文档", "失败", ex.Message)
            Throw New McpException($"获取活动文档失败: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function
End Class