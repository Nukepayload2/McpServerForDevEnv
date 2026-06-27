Imports EnvDTE80
Imports Newtonsoft.Json
Imports System.IO
Imports System.Net
Imports System.Threading

Public Class McpService
    Implements IDisposable

    Private ReadOnly _port As Integer
    Private ReadOnly _logger As IMcpLogger
    Private ReadOnly _dispatcher As IDispatcher
    Private _isRunning As Boolean = False
    Private ReadOnly _toolManager As VisualStudioToolManager
    Private ReadOnly _clipboard As IClipboard
    Private ReadOnly _interaction As IInteraction

    Private _listener As HttpListener
    Private _cts As CancellationTokenSource
    Private _listenerTask As Task
    Private ReadOnly _httpHandler As VisualStudioMcpHttpService

    ''' <summary>
    ''' 创建 MCP 服务实例。
    ''' </summary>
    ''' <param name="dte2">
    ''' 已废弃参数，保留仅为兼容 VSIX 调用方（McpWindowState.vb）。
    ''' dte2 只在工具数据上下文层（VisualStudioTools）使用，不应注入服务层。
    ''' 新调用方（独立 WPF 应用）请传 Nothing。
    ''' </param>
    Sub New(dte2 As DTE2, port As Integer, logger As IMcpLogger, dispatcher As IDispatcher, toolManager As VisualStudioToolManager, clipboard As IClipboard, interaction As IInteraction)
        _port = port
        _logger = logger
        _dispatcher = dispatcher
        _toolManager = toolManager
        _clipboard = clipboard
        _interaction = interaction
        _httpHandler = New VisualStudioMcpHttpService(_toolManager)
    End Sub

    ''' <summary>
    ''' 获取工具管理器实例
    ''' </summary>
    ''' <returns>工具管理器实例，如果服务未启动则返回 Nothing</returns>
    Public ReadOnly Property ToolManager As VisualStudioToolManager
        Get
            Return _toolManager
        End Get
    End Property

    Public Async Function StartAsync() As Task
        If _isRunning Then
            Throw New InvalidOperationException("服务已经在运行中")
        End If

        Try
            Await StartHttpListenerAsync()

            _isRunning = True

            _logger?.LogMcpRequest("MCP服务", "启动", $"HTTP端点: http://localhost:{_port}/mcp/")
        Catch ex As Exception
            _isRunning = False
            _logger?.LogMcpRequest("MCP服务", "启动失败", ex.Message)
            If TypeOf ex IsNot HttpListenerException Then
                Throw New Exception($"启动 MCP 服务失败: {ex.Message}", ex)
            Else
                Throw
            End If
        End Try
    End Function

    Private Async Function StartHttpListenerAsync() As Task
        Dim accessErr As HttpListenerException = Nothing
        Try
            If _toolManager Is Nothing Then
                Throw New InvalidOperationException("工具管理器未传入，无法启动 MCP 服务")
            End If

            _listener = New HttpListener()
            _listener.Prefixes.Add($"http://+:{_port}/mcp/")
            _listener.Start()

            _cts = New CancellationTokenSource()
            _listenerTask = Task.Run(Function() ListenLoopAsync(_cts.Token))

        Catch ex As HttpListenerException
            accessErr = ex
        Catch ex As Exception
            Throw New Exception($"启动 HTTP 服务失败: {ex.Message}", ex)
        End Try

        If accessErr IsNot Nothing Then
            ' 生成 netsh 命令
            Dim command = $"netsh http add urlacl url=http://+:{_port}/mcp/ user=""%USERDOMAIN%\%USERNAME%""
netsh http add iplisten ipaddress=127.0.0.1:{_port}"

            Dim clipboardSuccess = TryCopyToClipboard(command, "MCP服务")

            Dim userMessage As String
            If clipboardSuccess Then
                userMessage = $"端口 {_port} 需要授权。已将命令复制到剪贴板，请以管理员身份运行命令提示符并粘贴执行："
            Else
                userMessage = $"端口 {_port} 需要授权。剪贴板操作失败，请手动执行以下命令："
            End If

            _logger?.LogMcpRequest("MCP服务", "权限不足", userMessage)

            ' 在UI线程显示弹出窗口
            Await ShowUserMessageWindowAsync("端口授权", userMessage, command)
            Throw accessErr
        End If
    End Function

    Private Async Function ListenLoopAsync(token As CancellationToken) As Task
        While _listener.IsListening AndAlso Not token.IsCancellationRequested
            Try
                Dim context = Await _listener.GetContextAsync()
                ' fire-and-forget 每个请求，不阻塞监听循环
                Dim forget = Task.Run(Function() SafeHandleRequestAsync(context, token))
            Catch ex As HttpListenerException
                ' listener 停止时正常退出
                Exit While
            End Try
        End While
    End Function

    Private Async Function SafeHandleRequestAsync(ctx As HttpListenerContext, token As CancellationToken) As Task
        Try
            Await HandleRequestAsync(ctx)
        Catch ex As Exception
            _logger?.LogMcpRequest("MCP服务", "请求处理异常", ex.Message)
            Try
                ctx.Response.StatusCode = 500
                ctx.Response.Close()
            Catch
            End Try
        End Try
    End Function

    Private Async Function HandleRequestAsync(ctx As HttpListenerContext) As Task
        Dim path = ctx.Request.Url.AbsolutePath.TrimStart("/"c).TrimEnd("/"c)

        ' 路由：GET status → 健康检查；POST mcp → MCP 请求
        If String.Equals(path, "mcp/status", StringComparison.OrdinalIgnoreCase) _
           AndAlso ctx.Request.HttpMethod = "GET" Then
            Await WriteJsonAsync(ctx.Response, _httpHandler.GetStatus())
        ElseIf String.Equals(path, "mcp", StringComparison.OrdinalIgnoreCase) _
           AndAlso ctx.Request.HttpMethod = "POST" Then
            Await HandleMcpRequestAsync(ctx)
        Else
            ctx.Response.StatusCode = 404
            ctx.Response.Close()
        End If
    End Function

    Private Async Function HandleMcpRequestAsync(ctx As HttpListenerContext) As Task
        Dim requestBody As String
        Dim encoding = If(ctx.Request.ContentEncoding, System.Text.Encoding.UTF8)
        Using reader As New StreamReader(ctx.Request.InputStream, encoding)
            requestBody = Await reader.ReadToEndAsync()
        End Using

        Dim request As JsonRpcRequest = Nothing
        If Not String.IsNullOrEmpty(requestBody) Then
            request = JsonConvert.DeserializeObject(Of JsonRpcRequest)(requestBody)
        End If

        Dim response = Await _httpHandler.ProcessMcpRequest(request)

        ' 通知（response 为 Nothing）→ 204 No Content
        If response Is Nothing Then
            ctx.Response.StatusCode = 204
            ctx.Response.Close()
            Return
        End If

        Await WriteJsonAsync(ctx.Response, response)
    End Function

    Private Async Function WriteJsonAsync(response As HttpListenerResponse, obj As Object) As Task
        response.ContentType = "application/json"
        response.ContentEncoding = System.Text.Encoding.UTF8
        Dim json = JsonConvert.SerializeObject(obj, Formatting.Indented)
        Dim bytes = System.Text.Encoding.UTF8.GetBytes(json)
        response.ContentLength64 = bytes.Length
        Await response.OutputStream.WriteAsync(bytes, 0, bytes.Length)
        response.Close()
    End Function

    Public Sub [Stop]()
        If Not _isRunning Then
            Return
        End If

        _isRunning = False

        Try
            _cts?.Cancel()
            _listener?.[Stop]()
            _listener?.Close()
            _listener = Nothing

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

    Private Async Function ShowUserMessageWindowAsync(title As String, message As String, command As String) As Task
        Try
            Await _dispatcher.InvokeAsync(
            Sub()
                Try
                    System.Media.SystemSounds.Exclamation.Play()
                    _interaction.ShowCopyCommandDialog(title, message, command)
                Catch ex As Exception
                    _logger?.LogMcpRequest("MCP服务", "显示消息窗口失败", ex.Message)
                End Try
            End Sub)
        Catch ex As Exception
            _logger?.LogMcpRequest("MCP服务", "调用UI线程失败", ex.Message)
        End Try
    End Function

    Private Function TryCopyToClipboard(command As String, operation As String) As Boolean
        Try
            ' 尝试复制到剪贴板（带重试机制）
            Dim maxRetries = 3
            Dim retryDelay = 100 ' 初始延迟 100ms

            For retry = 1 To maxRetries
                Try
                    _clipboard.SetText(command)
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
