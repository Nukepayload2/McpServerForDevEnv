Imports EnvDTE80
Imports System.Windows.Threading
Imports System.ServiceModel
Imports System.ServiceModel.Description
Imports System.ServiceModel.Web
Imports System.ServiceModel.Channels
Imports System.Diagnostics
Imports System.ServiceModel.Configuration
Imports System.ServiceModel.Dispatcher
Imports Newtonsoft.Json
Imports System.Xml
Imports System.IO
Imports System.Net
Imports System.Reflection

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

            _logger?.LogMcpRequest("MCP服务", "启动", $"HTTP端点: http://localhost:{_port}/mcp/")
        Catch ex As Exception
            _isRunning = False
            _logger?.LogMcpRequest("MCP服务", "启动失败", ex.Message)
            Throw New Exception($"启动 MCP 服务失败: {ex.Message}", ex)
        End Try
    End Sub

    Private Sub StartWcfService()
        Try
            Dim baseAddress As New Uri($"http://localhost:{_port}/")

            ' 创建服务实例并传递依赖项
            Dim vsTools As New VisualStudioTools(_dte2, _dispatcher, _logger)
            Dim vsMcpTools As New VisualStudioMcpTools(_logger, vsTools, _permissionHandler)
            Dim vsMcpHttp As New VisualStudioMcpHttpService(vsMcpTools)
            _serviceHost = New ServiceHost(vsMcpHttp, baseAddress)

            ' 配置服务调试行为
            Dim debugBehavior As New ServiceDebugBehavior With {
                .IncludeExceptionDetailInFaults = True,
                .HttpHelpPageEnabled = False
            }
            _serviceHost.Description.Behaviors.Remove(Of ServiceDebugBehavior)()
            _serviceHost.Description.Behaviors.Add(debugBehavior)

            ' 配置服务元数据行为
            Dim metadataBehavior As New ServiceMetadataBehavior With {
                .HttpGetEnabled = False,
                .HttpsGetEnabled = False
            }
            _serviceHost.Description.Behaviors.Remove(Of ServiceMetadataBehavior)()
            _serviceHost.Description.Behaviors.Add(metadataBehavior)

            ' 添加服务端点 - 使用WebHttpBinding配合自定义JSON行为
            Dim binding As New WebHttpBinding With {
                .TransferMode = TransferMode.Buffered,
                .MaxReceivedMessageSize = 2147483647,
                .ReaderQuotas = New System.Xml.XmlDictionaryReaderQuotas With {
                    .MaxStringContentLength = 2147483647,
                    .MaxArrayLength = 2147483647
                }
            }

            Dim endpoint = _serviceHost.AddServiceEndpoint(GetType(VisualStudioMcpHttpService), binding, "mcp")

            ' 为端点启用自定义Newtonsoft.Json行为
            Dim jsonBehavior As New NewtonsoftJsonBehavior()
            endpoint.Behaviors.Add(jsonBehavior)

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

' 自定义JSON消息格式化器 - 使用Newtonsoft.Json处理序列化
Public Class NewtonsoftJsonDispatchFormatter
    Implements IDispatchMessageFormatter

    Private ReadOnly _operation As OperationDescription
    Private ReadOnly _isRequest As Boolean

    Public Sub New(operation As OperationDescription, isRequest As Boolean)
        _operation = operation
        _isRequest = isRequest
    End Sub

    Public Sub DeserializeRequest(message As Message, parameters() As Object) Implements IDispatchMessageFormatter.DeserializeRequest
        If _isRequest AndAlso parameters.Length > 0 Then
            Try
                ' 使用反射获取原始消息体
                Dim jsonContent As String = String.Empty

                If message IsNot Nothing Then
                    ' 获取RequestContext
                    Dim requestContext = OperationContext.Current.RequestContext
                    Dim requestMessage = requestContext.RequestMessage

                    ' 使用反射获取MessageData属性
                    Dim messageDataProperty = requestMessage.GetType().GetProperty("MessageData", Reflection.BindingFlags.Public Or Reflection.BindingFlags.NonPublic Or Reflection.BindingFlags.Instance)
                    If messageDataProperty IsNot Nothing Then
                        Dim messageData = messageDataProperty.GetValue(requestMessage)

                        ' 获取Buffer属性
                        Dim bufferProperty = messageData.GetType().GetProperty("Buffer")
                        If bufferProperty IsNot Nothing Then
                            Dim buffer = bufferProperty.GetValue(messageData)

                            ' 转换为ArraySegment<byte>
                            If TypeOf buffer Is ArraySegment(Of Byte) Then
                                Dim arraySegment = DirectCast(buffer, ArraySegment(Of Byte))
                                If arraySegment.Count > 0 Then
                                    jsonContent = System.Text.Encoding.UTF8.GetString(arraySegment.Array, arraySegment.Offset, arraySegment.Count)
                                End If
                            ElseIf TypeOf buffer Is Byte() Then
                                Dim byteArray = DirectCast(buffer, Byte())
                                If byteArray.Length > 0 Then
                                    jsonContent = System.Text.Encoding.UTF8.GetString(byteArray)
                                End If
                            End If
                        End If
                    End If
                End If

                ' 检查是否是POST请求的JSON-RPC调用
                If Not String.IsNullOrEmpty(jsonContent) Then
                    ' 使用Newtonsoft.Json反序列化为JsonRpcRequest对象
                    Dim request As JsonRpcRequest = JsonConvert.DeserializeObject(Of JsonRpcRequest)(jsonContent)
                    parameters(0) = request
                Else
                    parameters(0) = Nothing
                End If
            Catch ex As Exception
                ' 如果反序列化失败，设置为null
                parameters(0) = Nothing
                Debug.WriteLine($"[NewtonsoftJsonDispatchFormatter] DeserializeRequest error: {ex.Message}")
            End Try
        End If
    End Sub

    Private Class NoFault
        Inherits MessageFault

        Public Overrides ReadOnly Property Code As FaultCode
            Get
                Return New FaultCode("Sender")
            End Get
        End Property

        Public Overrides ReadOnly Property HasDetail As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides ReadOnly Property Reason As FaultReason
            Get
                Return New FaultReason("No fault")
            End Get
        End Property

        Protected Overrides Sub OnWriteDetailContents(writer As XmlDictionaryWriter)
        End Sub
    End Class

    ' 自定义RawBodyWriter - 用于写入原始字节数据到消息体
    Private Class RawBodyWriter
        Inherits BodyWriter

        Private ReadOnly _content As Byte()

        Public Sub New(content As Byte())
            MyBase.New(True) ' isBuffered = true
            _content = content
        End Sub

        Protected Overrides Sub OnWriteBodyContents(writer As XmlDictionaryWriter)
            writer.WriteStartElement("Binary")
            writer.WriteBase64(_content, 0, _content.Length)
            writer.WriteEndElement()
        End Sub
    End Class

    Public Function SerializeReply(messageVersion As MessageVersion, parameters() As Object, result As Object) As Message Implements IDispatchMessageFormatter.SerializeReply
        Try
            ' 如果结果为null（通知），返回空响应
            If result Is Nothing Then
                If WebOperationContext.Current IsNot Nothing Then
                    WebOperationContext.Current.OutgoingResponse.StatusCode = System.Net.HttpStatusCode.NoContent
                End If
                Return Message.CreateMessage(messageVersion, "")
            End If

            ' 使用Newtonsoft.Json序列化器
            Dim serializer As New JsonSerializer()
            serializer.Converters.Add(New Newtonsoft.Json.Converters.IsoDateTimeConverter() With {
                .DateTimeFormat = "yyyy-MM-ddTHH:mm:ssZ",
                .DateTimeStyles = Globalization.DateTimeStyles.AdjustToUniversal
            })

            ' 使用MemoryStream和RawBodyWriter创建响应
            Using ms As New MemoryStream()
                Using sw As New StreamWriter(ms, System.Text.Encoding.UTF8)
                    Using writer As New JsonTextWriter(sw)
                        serializer.Serialize(writer, result)
                        sw.Flush()
                        Dim body() As Byte = ms.ToArray()

                        ' 创建响应消息
                        Dim replyMessage = Message.CreateMessage(messageVersion, _operation.Messages(1).Action, New RawBodyWriter(body))
                        replyMessage.Properties.Add(WebBodyFormatMessageProperty.Name, New WebBodyFormatMessageProperty(WebContentFormat.Raw))

                        ' 设置响应头
                        Dim respProp As New HttpResponseMessageProperty()
                        respProp.Headers(HttpResponseHeader.ContentType) = "application/json"
                        replyMessage.Properties.Add(HttpResponseMessageProperty.Name, respProp)

                        Return replyMessage
                    End Using
                End Using
            End Using
        Catch ex As Exception
            ' 如果序列化失败，返回错误响应
            Try
                Dim errorResponse As String = JsonConvert.SerializeObject(New With {Key .error = ex.Message})
                Dim errorBytes() As Byte = System.Text.Encoding.UTF8.GetBytes(errorResponse)

                Dim errorMessage = Message.CreateMessage(messageVersion, _operation.Messages(1).Action, New RawBodyWriter(errorBytes))
                errorMessage.Properties.Add(WebBodyFormatMessageProperty.Name, New WebBodyFormatMessageProperty(WebContentFormat.Raw))

                Dim respProp As New HttpResponseMessageProperty()
                respProp.Headers(HttpResponseHeader.ContentType) = "application/json"
                errorMessage.Properties.Add(HttpResponseMessageProperty.Name, respProp)

                If WebOperationContext.Current IsNot Nothing Then
                    WebOperationContext.Current.OutgoingResponse.StatusCode = System.Net.HttpStatusCode.InternalServerError
                End If

                Return errorMessage
            Catch
                ' 如果连错误响应都无法创建，返回最基本的错误消息
                Return Message.CreateMessage(messageVersion, "Serialization Error")
            End Try
        End Try
    End Function
End Class

' 自定义WebHttpBehavior - 使用Newtonsoft.Json格式化器
Public Class NewtonsoftJsonBehavior
    Inherits WebHttpBehavior

    Protected Overrides Function GetRequestDispatchFormatter(operationDescription As OperationDescription, endpoint As ServiceEndpoint) As IDispatchMessageFormatter
        Return New NewtonsoftJsonDispatchFormatter(operationDescription, True)
    End Function

    Protected Overrides Function GetReplyDispatchFormatter(operationDescription As OperationDescription, endpoint As ServiceEndpoint) As IDispatchMessageFormatter
        Return New NewtonsoftJsonDispatchFormatter(operationDescription, False)
    End Function
End Class

