Imports EnvDTE80
Imports System.Windows.Threading
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

        Catch ex As AddressAccessDeniedException
            ' 生成 netsh 命令
            Dim command = $"netsh http add urlacl url=http://+:{_port}/mcp/ user=""%USERDOMAIN%\%USERNAME%""
netsh http add iplisten ipaddress=127.0.0.1:{_port}"

            ' 尝试复制到剪贴板
            Dim clipboardSuccess = TryCopyToClipboard(command, "MCP服务")

            ' 构建用户提示信息
            Dim userMessage As String
            If clipboardSuccess Then
                userMessage = $"端口 {_port} 需要管理员权限。已将命令复制到剪贴板，请以管理员身份运行命令提示符并粘贴执行。"
            Else
                userMessage = $"端口 {_port} 需要管理员权限。剪贴板操作失败，请手动复制以下命令：{Environment.NewLine}{command}"
            End If

            _logger?.LogMcpRequest("MCP服务", "权限不足", userMessage)
            Throw New Exception(userMessage, ex)

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

    Private Function TryCopyToClipboard(command As String, operation As String) As Boolean
        Try
            ' 尝试复制到剪贴板（带重试机制）
            Dim maxRetries = 3
            Dim retryDelay = 100 ' 初始延迟 100ms

            For retry = 1 To maxRetries
                Try
                    Clipboard.SetDataObject(command, True)
                    _logger?.LogMcpRequest(operation, "剪贴板操作", "命令已成功复制到剪贴板")
                    Return True
                Catch clipboardEx As Exception
                    _logger?.LogMcpRequest(operation, "剪贴板操作失败",
                        $"第 {retry} 次尝试失败: {clipboardEx.Message}")

                    If retry < maxRetries Then
                        System.Threading.Thread.Sleep(retryDelay)
                        retryDelay *= 2 ' 指数退避
                    End If
                End Try
            Next

            _logger?.LogMcpRequest(operation, "剪贴板操作失败", "已达到最大重试次数")
            Return False
        Catch ex As Exception
            _logger?.LogMcpRequest(operation, "剪贴板操作异常", ex.Message)
            Return False
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            [Stop]()
        Catch ex As Exception
            ' 忽略停止时的错误
        End Try
    End Sub
End Class
