Imports System.ComponentModel

Public Class VisualStudioMcpTools
    Private ReadOnly _logger As IMcpLogger
    Private ReadOnly _vsTools As VisualStudioTools
    Private ReadOnly _permissionHandler As IMcpPermissionHandler

    Public Sub New(logger As IMcpLogger, vsTools As VisualStudioTools, permissionHandler As IMcpPermissionHandler)
        _logger = logger
        _vsTools = vsTools
        _permissionHandler = permissionHandler
    End Sub

    Public Sub Log(action As String, result As String, details As String)
        _logger.LogMcpRequest(action, result, details)
    End Sub

    ''' <summary>
    ''' 构建整个解决方案
    ''' </summary>
    Public Async Function BuildSolution(
        <Description("构建配置 (Debug/Release)")> Optional configuration As String = "Debug"
    ) As Task(Of BuildResult)
        Try
            ' 检查权限
            If Not CheckPermission("build_solution", "构建解决方案") Then
                Throw New McpException("权限被拒绝", McpErrorCode.InvalidParams)
            End If

            _logger?.LogMcpRequest("构建解决方案", "开始", $"配置: {configuration}")

            Dim result = Await _vsTools.BuildSolutionAsync(configuration)

            _logger?.LogMcpRequest("构建解决方案", If(result.Success, "成功", "失败"), result.Message)

            Return result

        Catch ex As Exception
            _logger?.LogMcpRequest("构建解决方案", "失败", ex.Message)
            Throw New McpException($"构建解决方案失败: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function

    ''' <summary>
    ''' 构建指定项目
    ''' </summary>
    Public Async Function BuildProject(
        <Description("项目名称")> projectName As String,
        <Description("构建配置 (Debug/Release)")> Optional configuration As String = "Debug"
    ) As Task(Of BuildResult)
        Try
            ' 检查权限
            If Not CheckPermission("build_project", "构建项目") Then
                Throw New McpException("权限被拒绝", McpErrorCode.InvalidParams)
            End If

            If String.IsNullOrEmpty(projectName) Then
                Throw New McpException("项目名称不能为空", McpErrorCode.InvalidParams)
            End If

            _logger?.LogMcpRequest("构建项目", "开始", $"项目: {projectName}, 配置: {configuration}")

            Dim result = Await _vsTools.BuildProjectAsync(projectName, configuration)

            _logger?.LogMcpRequest("构建项目", If(result.Success, "成功", "失败"), result.Message)

            Return result

        Catch ex As Exception
            _logger?.LogMcpRequest("构建项目", "失败", ex.Message)
            Throw New McpException($"构建项目失败: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function

    ''' <summary>
    ''' 获取当前的错误和警告列表
    ''' </summary>
    Public Function GetErrorList(
        <Description("过滤级别 (Error/Warning/Message/All)")> Optional severity As String = "All"
    ) As ErrorListResponse
        Try
            ' 检查权限
            If Not CheckPermission("get_error_list", "获取错误列表") Then
                Throw New McpException("权限被拒绝", McpErrorCode.InvalidParams)
            End If

            Dim result = _vsTools.GetErrorList(severity)

            Return New ErrorListResponse With {
                .Errors = result.Errors.ToArray(),
                .Warnings = result.Warnings.ToArray(),
                .TotalCount = result.Errors.Count + result.Warnings.Count
            }

        Catch ex As Exception
            _logger?.LogMcpRequest("获取错误列表", "失败", ex.Message)
            Throw New McpException($"获取错误列表失败: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function

    ''' <summary>
    ''' 获取当前解决方案信息
    ''' </summary>
    Public Function GetSolutionInfo() As SolutionInfoResponse
        Try
            ' 检查权限
            If Not CheckPermission("get_solution_info", "获取解决方案信息") Then
                Throw New McpException("权限被拒绝", McpErrorCode.InvalidParams)
            End If

            Return _vsTools.GetSolutionInformation()

        Catch ex As Exception
            _logger?.LogMcpRequest("获取解决方案信息", "失败", ex.Message)
            Throw New McpException($"获取解决方案信息失败: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function

    ''' <summary>
    ''' 获取当前活动文档的信息
    ''' </summary>
    Public Function GetActiveDocument() As ActiveDocumentResponse
        Try
            ' 检查权限
            If Not CheckPermission("get_active_document", "获取活动文档") Then
                Throw New McpException("权限被拒绝", McpErrorCode.InvalidParams)
            End If

            Return _vsTools.GetActiveDocument()

        Catch ex As Exception
            _logger?.LogMcpRequest("获取活动文档", "失败", ex.Message)
            Throw New McpException($"获取活动文档失败: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function

    Private Function CheckPermission(featureName As String, operationDescription As String) As Boolean
        Try
            Return _permissionHandler.CheckPermission(featureName, operationDescription)
        Catch ex As Exception
            _logger?.LogMcpRequest("权限检查", "异常", ex.Message)
            Return False
        End Try
    End Function
End Class