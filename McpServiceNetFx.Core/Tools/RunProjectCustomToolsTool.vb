''' <summary>
''' 运行指定项目自定义工具的工具
''' </summary>
Public Class RunProjectCustomToolsTool
    Inherits VisualStudioToolBase

    ''' <summary>
    ''' 创建工具实例
    ''' </summary>
    Public Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, permissionHandler)
    End Sub

    Public Overrides ReadOnly Property ToolDefinition As New ToolDefinition With {
        .Name = "run_project_custom_tools",
        .Description = "运行指定项目的自定义工具",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition) From {
                {"projectName", New PropertyDefinition With {
                    .Type = "string",
                    .Description = "项目名称"
                }}
            },
            .Required = {"projectName"}
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

            ' 验证必需参数
            ValidateRequiredArguments(arguments, "projectName")

            ' 获取参数
            Dim projectName = arguments("projectName").ToString()

            If String.IsNullOrEmpty(projectName) Then
                Throw New McpException("项目名称不能为空", McpErrorCode.InvalidParams)
            End If

            LogOperation("运行项目自定义工具", "开始", $"项目: {projectName}")

            Dim result = Await RunCustomToolForProjectAsync(projectName)

            LogOperation("运行项目自定义工具", If(result.Success, "成功", "失败"), result.Message)

            Return result

        Catch ex As Exception
            LogOperation("运行项目自定义工具", "失败", ex.Message)
            Throw New McpException($"运行项目自定义工具失败: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function

    ''' <summary>
    ''' 运行特定项目中所有 resx 文件的自定义工具
    ''' </summary>
    ''' <param name="projectName">项目名称</param>
    ''' <returns>执行结果</returns>
    Private Async Function RunCustomToolForProjectAsync(projectName As String) As Task(Of RunCustomToolsResult)
        Return Await _vsTools.RunProjectCustomToolsAsync(projectName)
    End Function
End Class
