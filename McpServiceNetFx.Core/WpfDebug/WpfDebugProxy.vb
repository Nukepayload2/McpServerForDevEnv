Imports System.IO
Imports System.IO.Pipes
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Collections.Concurrent
Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

' WPF 调试主控侧 pipe client：连被控端 named pipe server，封装连接/断开、
' 发请求收响应、握手、订阅缓存事件。作为 WPF 调试工具（#23）的数据上下文。
'
' 【设计要点】
' 1. pipe 名带 PID（WpfDebugProtocol.GetPipeNameForPid）。两种连接入口（构造时必须指定目标）：
'    - New(targetPid) + ConnectAsync() ：连指定 PID 的被控端（用户从候选列表选了目标，生产路径）。
'    - New(pipeName) + ConnectAsync()  ：直接指定 pipe 名（测试/定制用）。
'    单活跃连接语义：同时只持有一个连接。
' 2. 全程异步（ConnectAsync / ReadAsync / WriteAsync），连接等待可超时可取消，
'    绝不 .Result / .Wait() 同步阻塞（避免 UI 线程死锁）。
' 3. 后台读循环收两类帧：WpfDebugResponse（按 id 配对 pending 请求）和
'    WpfDebugEvent（被控端主动推，缓存供 list_events 工具读）。
'    用 JObject 先读再按字段判断类型——响应有 "id"，事件有 "event"。
' 4. 握手：连接成功后被控端先发一帧握手（JObject，非响应），主控先读这一帧解析出
'    pid/主窗口标题/协议版本/可执行路径。之后才进入请求/响应循环。
' 5. 连接失败/握手失败/读写异常：置为断开状态 + 抛异常给调用方（UI 报"未连接被控端"风格），
'    不裸崩。后台读循环异常会触发 Disconnected 事件，由 WpfDebugConnectionMonitor 处理清理。

''' <summary>
''' WPF 调试被控端代理（named pipe client）。
''' </summary>
Public Class WpfDebugProxy
    Implements IDisposable

    ' 构造时由 PID/pipeName 指定；非 ReadOnly 仅为构造赋值便利。
    Private _pipeName As String

    Private _client As NamedPipeClientStream
    ' 注意：读写全程直接用底层 _client stream + MessageFramer，不套 StreamReader/StreamWriter——
    ' 它们的内部缓冲会和 MessageFramer 的精确字节读取互相干扰。
    Private _readLoop As Task
    Private _cts As CancellationTokenSource
    Private _disposed As Boolean

    ' 待响应请求：id -> TaskCompletionSource。后台读循环按 id 配对。
    Private ReadOnly _pending As New ConcurrentDictionary(Of String, TaskCompletionSource(Of WpfDebugResponse))()

    ' 自增请求 id。
    Private _nextId As Integer

    ' 握手信息，连接成功后填充。
    Private _handshake As WpfDebugHandshakeInfo

    ' 事件缓存（被控端主动推），供 list_events 工具读。容量受限，超出丢弃最早的。
    Private ReadOnly _events As New ConcurrentQueue(Of WpfDebugEvent)()
    Private Const MaxCachedEvents As Integer = 500

    ' 当前是否已连接（pipe 处于连接状态且握手完成）。
    Private _isConnected As Boolean

    ''' <summary>连接断开时触发（读写异常、对端关闭、Dispose）。由 WpfDebugConnectionMonitor 订阅做清理。</summary>
    Public Event Disconnected As EventHandler(Of EventArgs)

    ''' <summary>
    ''' 指定目标 PID 构造。<see cref="ConnectAsync()"/> 会连 <see cref="WpfDebugProtocol.GetPipeNameForPid"/>(targetPid)。
    ''' 生产路径：主控 UI 从候选列表选中目标 PID 后用此构造。
    ''' </summary>
    Public Sub New(targetPid As Integer)
        If targetPid <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(targetPid))
        _pipeName = WpfDebugProtocol.GetPipeNameForPid(targetPid)
    End Sub

    ''' <summary>指定 pipe 名构造（测试或定制用；生产路径用 PID 构造）。</summary>
    Public Sub New(pipeName As String)
        If String.IsNullOrEmpty(pipeName) Then Throw New ArgumentNullException(NameOf(pipeName))
        _pipeName = pipeName
    End Sub

    ''' <summary>当前是否已连接被控端（pipe 连接且握手完成）。</summary>
    Public ReadOnly Property IsConnected As Boolean
        Get
            Return _isConnected AndAlso _client IsNot Nothing AndAlso _client.IsConnected
        End Get
    End Property

    ''' <summary>握手信息。未连接时为 Nothing。</summary>
    Public ReadOnly Property Handshake As WpfDebugHandshakeInfo
        Get
            Return _handshake
        End Get
    End Property

    ''' <summary>
    ''' 实际使用的 pipe 名。构造时（按 PID 或直接 pipe 名）指定。
    ''' </summary>
    Public ReadOnly Property PipeName As String
        Get
            Return _pipeName
        End Get
    End Property

    ''' <summary>
    ''' 异步连接被控端 pipe server 并完成握手。
    ''' 连接等待可超时（默认 5 秒），可取消。
    ''' 连接失败/握手失败抛异常给调用方（UI 报"未连接被控端"风格）。
    '''
    ''' 连接的 pipe 名由构造时指定（<c>New(targetPid)</c> 或 <c>New(pipeName)</c>）。
    ''' 若未指定（_pipeName 为空，理论上构造后不会发生）抛 <see cref="ArgumentNullException"/> 作防御。
    ''' </summary>
    ''' <param name="connectTimeoutMs">单次连接尝试的等待超时（毫秒）。被控端未启动时在此时间内连不上即抛 TimeoutException。</param>
    ''' <param name="cancellationToken">取消令牌。</param>
    Public Async Function ConnectAsync(Optional connectTimeoutMs As Integer = 5000, Optional cancellationToken As CancellationToken = Nothing) As Task
        If _disposed Then Throw New ObjectDisposedException(NameOf(WpfDebugProxy))
        If IsConnected Then Return

        ' 构造必须指定目标 pid 或 pipeName；_pipeName 为空是构造误用，防御抛异常。
        If String.IsNullOrEmpty(_pipeName) Then
            Throw New ArgumentNullException(NameOf(_pipeName), "WpfDebugProxy 构造时未指定目标 pipe 名；请用 New(targetPid) 或 New(pipeName) 构造。")
        End If

        ' 清理可能的旧状态。
        DisconnectInternal()

        Await ConnectCoreAsync(_pipeName, connectTimeoutMs, cancellationToken).ConfigureAwait(False)
    End Function

    ''' <summary>
    ''' 实际连接 + 握手 + 起读循环的核心逻辑（与 pipe 名来源解耦）。
    ''' </summary>
    Private Async Function ConnectCoreAsync(pipeName As String, connectTimeoutMs As Integer, cancellationToken As CancellationToken) As Task
        Dim client As NamedPipeClientStream = Nothing
        Try
            client = New NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous)

            ' 连接等待：被控端没起则在此超时。用 CreateLinkedTokenSource 让取消同时作用于连接。
            Using linked As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                linked.CancelAfter(connectTimeoutMs)
                Try
                    Await client.ConnectAsync(linked.Token).ConfigureAwait(False)
                Catch ex As OperationCanceledException
                    If cancellationToken.IsCancellationRequested Then Throw
                    Throw New TimeoutException($"连接被控端超时（{connectTimeoutMs}ms），请确认被控 WPF 程序已启动调试模块", ex)
                End Try
            End Using

            _client = client
            client = Nothing ' 所有权转移，防止下面 Finally 误 Dispose

            ' 握手：被控端连接后先发一帧。用 MessageFramer 读原始 JObject 再按字段解析。
            Dim handshakeFrame As JObject = Await MessageFramer.ReadAsync(Of JObject)(_client, cancellationToken).ConfigureAwait(False)
            If handshakeFrame Is Nothing Then
                Throw New IOException("被控端连接后未发送握手帧")
            End If
            _handshake = WpfDebugResultReader.ParseHandshake(handshakeFrame)

            ' 协议版本校验：被控端版本与主控预期不一致则拒绝（避免协议错配）。
            ' 这里只做存在性检查 + 简单等值；未来协议演进再细化。
            If String.IsNullOrEmpty(_handshake.ProtocolVersion) Then
                Throw New IOException("被控端握手缺少协议版本")
            End If

            _isConnected = True
            _nextId = 0

            ' 启动后台读循环（收响应/事件）。
            _cts = New CancellationTokenSource()
            _readLoop = Task.Run(AddressOf ReadLoop)
        Finally
            ' ConnectAsync 抛异常时清理半成品连接。
            client?.Dispose()
            If Not _isConnected Then DisconnectInternal()
        End Try
    End Function

    ''' <summary>
    ''' 断开连接。幂等。不抛异常。
    ''' </summary>
    Public Sub Disconnect()
        DisconnectInternal()
    End Sub

    ''' <summary>
    ''' 发请求给被控端并等待对应响应。<paramref name="methodParams"/> 为业务参数（将序列化进 request.params）。
    ''' 纯异步：等响应期间不阻塞调用线程。
    ''' </summary>
    ''' <param name="method">方法名，取 <see cref="WpfDebugMethods"/> 常量。</param>
    ''' <param name="methodParams">方法参数对象（将被 JObject.FromObject 包成 params）；Nothing 表示无参。</param>
    ''' <param name="cancellationToken">取消令牌。</param>
    ''' <returns>被控端响应。调用方再用 <see cref="WpfDebugResultReader"/> 取业务返回值。</returns>
    Public Async Function SendRequestAsync(method As String, Optional methodParams As Object = Nothing, Optional cancellationToken As CancellationToken = Nothing) As Task(Of WpfDebugResponse)
        If _disposed Then Throw New ObjectDisposedException(NameOf(WpfDebugProxy))
        If String.IsNullOrEmpty(method) Then Throw New ArgumentNullException(NameOf(method))
        If Not IsConnected Then Throw New InvalidOperationException("未连接被控端")

        Dim id As String = Interlocked.Increment(_nextId).ToString()

        Dim request As New WpfDebugRequest With {.Id = id, .Method = method}
        If methodParams IsNot Nothing Then
            request.Params = JObject.FromObject(methodParams)
        End If

        Dim tcs As New TaskCompletionSource(Of WpfDebugResponse)()
        _pending(id) = tcs

        Try
            ' 取消令牌联动：取消时让 pending 的 tcs 失败。Register 返回 CancellationTokenRegistration，
            ' 本地持有它随 tcs 一起离开作用域；这里不需要显式 Dispose（请求生命周期短）。
            Dim registration As CancellationTokenRegistration = Nothing
            If cancellationToken.CanBeCanceled Then
                registration = cancellationToken.Register(Sub()
                                                              Dim removed As TaskCompletionSource(Of WpfDebugResponse) = Nothing
                                                              If _pending.TryRemove(id, removed) Then
                                                                  removed.TrySetCanceled(cancellationToken)
                                                              End If
                                                          End Sub)
            End If

            Await MessageFramer.WriteAsync(_client, request, cancellationToken).ConfigureAwait(False)

            Dim response As WpfDebugResponse = Await tcs.Task.ConfigureAwait(False)

            ' 响应里带 Error 直接抛，由调用方按"被控端报错"风格处理。
            If response.Error IsNot Nothing Then
                Throw New WpfDebugRemoteException(response.Error.Code, response.Error.Message)
            End If

            Return response
        Finally
            Dim removed As TaskCompletionSource(Of WpfDebugResponse) = Nothing
            _pending.TryRemove(id, removed)
        End Try
    End Function

    ''' <summary>
    ''' 取当前缓存的事件副本（被控端主动推的异常/绑定错误/Trace/窗口开关等）。
    ''' 不清空缓存；list_events 工具调用时快照一份。
    ''' </summary>
    Public Function GetEvents() As IList(Of WpfDebugEvent)
        Return _events.ToArray()
    End Function

    ''' <summary>后台读循环：收帧、按字段分发到响应配对或事件缓存。连接断开/异常时退出。</summary>
    Private Async Function ReadLoop() As Task
        Dim token As CancellationToken = _cts.Token
        Try
            While Not token.IsCancellationRequested
                ' 先按 JObject 读，再按字段判断是响应（有 "id"）还是事件（有 "event"）。
                Dim frame As JObject = Await MessageFramer.ReadAsync(Of JObject)(_client, token).ConfigureAwait(False)
                If frame Is Nothing Then Exit While ' 对端干净关闭

                If frame("id") IsNot Nothing Then
                    ' 响应帧：反序列化成 WpfDebugResponse，按 id 配对 pending 请求。
                    Dim response As WpfDebugResponse = frame.ToObject(Of WpfDebugResponse)()
                    Dim tcs As TaskCompletionSource(Of WpfDebugResponse) = Nothing
                    If response.Id IsNot Nothing AndAlso _pending.TryGetValue(response.Id, tcs) Then
                        tcs.TrySetResult(response)
                    End If
                    ' id 不匹配的响应直接丢弃（理论不会发生）。
                ElseIf frame("event") IsNot Nothing Then
                    ' 事件帧：反序列化缓存。
                    Dim evt As WpfDebugEvent = frame.ToObject(Of WpfDebugEvent)()
                    EnqueueEvent(evt)
                End If
                ' 既无 id 也无 event 的帧丢弃（未知帧类型）。
            End While
        Catch ex As OperationCanceledException
            ' 正常关闭。
        Catch
            ' 读写异常：连接失效，让所有 pending 请求失败。
        Finally
            FailAllPending(New IOException("被控端连接已断开"))
            OnDisconnected()
        End Try
    End Function

    Private Sub EnqueueEvent(evt As WpfDebugEvent)
        _events.Enqueue(evt)
        ' 超容量丢弃最早的事件。
        While _events.Count > MaxCachedEvents
            Dim dropped As WpfDebugEvent = Nothing
            If Not _events.TryDequeue(dropped) Then Exit While
        End While
    End Sub

    Private Sub FailAllPending(ex As Exception)
        For Each pair In _pending
            Dim tcs As TaskCompletionSource(Of WpfDebugResponse) = Nothing
            If _pending.TryRemove(pair.Key, tcs) Then
                tcs.TrySetException(ex)
            End If
        Next
    End Sub

    Private Sub OnDisconnected()
        _isConnected = False
        RaiseEvent Disconnected(Me, EventArgs.Empty)
    End Sub

    Private Sub DisconnectInternal()
        _isConnected = False
        Try
            _cts?.Cancel()
        Catch
        End Try
        _cts = Nothing

        Try
            _client?.Dispose()
        Catch
        End Try
        _client = Nothing

        ' 不等后台循环，让它自己因取消退出。pending 请求由 ReadLoop 的 Finally 失败；
        ' 若 ReadLoop 还没跑到 Finally（极端竞态），这里兜底失败一次。
        FailAllPending(New IOException("被控端连接已断开"))
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        DisconnectInternal()
    End Sub
End Class

''' <summary>
''' 被控端回的 WpfDebugError 抛成本地异常，方便工具层 catch 后报"被控端报错"。
''' </summary>
Public Class WpfDebugRemoteException
    Inherits Exception

    Public ReadOnly Property Code As Integer

    Public Sub New(code As Integer, message As String)
        MyBase.New(If(message, $"被控端错误 [{code}]"))
        _code = code
    End Sub
End Class
