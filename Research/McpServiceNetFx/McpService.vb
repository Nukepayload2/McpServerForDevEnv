Imports EnvDTE80
Imports System.Windows.Threading
Imports System.ComponentModel
Imports System.ServiceModel
Imports System.ServiceModel.Description

Public Class McpService
    Implements IDisposable

    Private ReadOnly _dte2 As DTE2
    Private ReadOnly _port As Integer
    Private ReadOnly _logger As IMcpLogger
    Private ReadOnly _permissionHandler As IMcpPermissionHandler
    Private ReadOnly _dispatcher As Dispatcher
    Private _serviceHost As ServiceHost
    Private _isRunning As Boolean = False

    Sub New(dte2 As DTE2, port As Integer, logger As IMcpLogger, permissionHandler As IMcpPermissionHandler, dispatcher As Dispatcher)
        _dte2 = dte2
        _port = port
        _logger = logger
        _permissionHandler = permissionHandler
        _dispatcher = dispatcher
    End Sub

    Public Sub Start()
        If _isRunning Then
            Throw New InvalidOperationException("服务已经在运行中")
        End If

        Try
            ' 启动 WCF HTTP 服务
            StartWcfService()

            _isRunning = True

            _logger?.LogMcpRequest("MCP服务", "启动", $"HTTP端点: http://localhost:{_port}/mcp")
        Catch ex As Exception
            _isRunning = False
            _logger?.LogMcpRequest("MCP服务", "启动失败", ex.Message)
            Throw New Exception($"启动 MCP 服务失败: {ex.Message}", ex)
        End Try
    End Sub

    Private Sub StartWcfService()
        Try
            Dim baseAddress As New Uri($"http://localhost:{_port}/mcp")

            ' 创建服务实例并传递依赖项
            Dim vsTools As New VisualStudioTools(_dte2, _dispatcher, _logger)
            Dim vsMcpTools As New VisualStudioMcpTools(_logger, vsTools, _permissionHandler)
            Dim vsMcpHttp As New VisualStudioMcpHttpService(vsMcpTools)
            _serviceHost = New ServiceHost(vsMcpHttp, baseAddress)

            ' 添加服务端点
            _serviceHost.AddServiceEndpoint(GetType(IMcpHttpService), New WebHttpBinding(), "")

            ' 添加服务行为
            Dim behavior As New WebHttpBehavior With {
                .AutomaticFormatSelectionEnabled = True,
                .DefaultOutgoingRequestFormat = System.ServiceModel.Web.WebMessageFormat.Json,
                .DefaultOutgoingResponseFormat = System.ServiceModel.Web.WebMessageFormat.Json
            }

            ' 获取端点并添加行为
            Dim endpoint = _serviceHost.Description.Endpoints(0)
            endpoint.Behaviors.Add(behavior)

            ' 打开服务主机
            _serviceHost.Open()

        Catch ex As Exception
            Throw New Exception($"启动 WCF 服务失败: {ex.Message}", ex)
        End Try
    End Sub

    Public Sub [Stop]()
        If Not _isRunning Then
            Return
        End If

        _isRunning = False

        Try
            ' 停止 WCF 服务
            If _serviceHost IsNot Nothing Then
                _serviceHost.Close()
                _serviceHost = Nothing
            End If

            _logger?.LogMcpRequest("MCP服务", "停止", "HTTP服务已正常停止")

        Catch ex As Exception
            _logger?.LogMcpRequest("MCP服务", "停止异常", ex.Message)
        End Try
    End Sub

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return _isRunning
        End Get
    End Property

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            [Stop]()
        Catch ex As Exception
            ' 忽略停止时的错误
        End Try
    End Sub
End Class

Public Class VisualStudioMcpTools
    Private ReadOnly _logger As IMcpLogger
    Private ReadOnly _vsTools As VisualStudioTools
    Private ReadOnly _permissionHandler As IMcpPermissionHandler

    Public Sub New(logger As IMcpLogger, vsTools As VisualStudioTools, permissionHandler As IMcpPermissionHandler)
        _logger = logger
        _vsTools = vsTools
        _permissionHandler = permissionHandler
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

    Private Function CheckPermission(featureName As String, operationDescription As String) As Boolean
        Try
            Return _permissionHandler.CheckPermission(featureName, operationDescription)
        Catch ex As Exception
            _logger?.LogMcpRequest("权限检查", "异常", ex.Message)
            Return False
        End Try
    End Function
End Class