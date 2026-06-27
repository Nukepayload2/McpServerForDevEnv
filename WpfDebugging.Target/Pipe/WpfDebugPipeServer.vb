Imports System.Diagnostics
Imports System.IO.Pipes
Imports System.Threading
Imports System.Threading.Tasks
Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

' named pipe server：被控端在宿主进程内监听以 PID 拼出的 pipe 名，接受主控连接，
' 收 WpfDebugRequest → 按 Method 分派 → 回 WpfDebugResponse。
'
' 关键约束：
' 1. pipe 名带 PID（WpfDebugProtocol.GetPipeNameForPid(自身pid)），同机多 WPF 被控进程互不撞名。
'    maxNumberOfServerInstances=1：同一 PID 重复启动（理论不会）会撞名抛 IOException。
' 2. 全程异步（WaitForConnectionAsync / MessageFramer.ReadAsync / InvokeAsync），IPC 线程不碰 WPF 对象，
'    能力执行经 WpfDispatcher 切 UI 线程。绝不同步阻塞 UI 线程。
' 3. 单连接串行处理（maxNumberOfServerInstances=1 且本类一次只服务一个连接）。

''' <summary>
''' 被控端 named pipe server。
''' </summary>
Public Class WpfDebugPipeServer
    Implements IDisposable

    Private ReadOnly _dispatcher As WpfDispatcher
    Private ReadOnly _target As IWpfDebugTarget
    Private ReadOnly _pipeName As String

    Private _server As NamedPipeServerStream
    Private _listenTask As Task
    Private _cts As CancellationTokenSource
    Private _disposed As Boolean

    ''' <summary>
    ''' 用当前进程 PID 拼出的 pipe 名（<see cref="WpfDebugProtocol.GetPipeNameForPid"/>）构造。
    ''' 同机多 WPF 被控进程各自占自己 PID 的 pipe，互不撞名。
    ''' </summary>
    Public Sub New(dispatcher As WpfDispatcher, target As IWpfDebugTarget)
        Me.New(dispatcher, target, WpfDebugProtocol.GetPipeNameForPid(Process.GetCurrentProcess().Id))
    End Sub

    ''' <summary>
    ''' 指定 pipe 名构造（测试或定制用；生产路径用 PID 名）。
    ''' </summary>
    Public Sub New(dispatcher As WpfDispatcher, target As IWpfDebugTarget, pipeName As String)
        If dispatcher Is Nothing Then Throw New ArgumentNullException(NameOf(dispatcher))
        If target Is Nothing Then Throw New ArgumentNullException(NameOf(target))
        If String.IsNullOrEmpty(pipeName) Then Throw New ArgumentNullException(NameOf(pipeName))
        _dispatcher = dispatcher
        _target = target
        _pipeName = pipeName
    End Sub

    ''' <summary>实际使用的 pipe 名。</summary>
    Public ReadOnly Property PipeName As String
        Get
            Return _pipeName
        End Get
    End Property

    ''' <summary>
    ''' 创建底层 NamedPipeServerStream。maxNumberOfServerInstances=1：同 PID 重复实例占用同名 pipe 时即抛异常。
    ''' </summary>
    Protected Overridable Function CreateServerStream(pipeName As String) As NamedPipeServerStream
        ' PipeDirection.InOut、双工；maxNumberOfServerInstances=1 强制单实例；
        ' 使用当前进程的 SID 作为安全模板（生产可进一步收窄，调试场景本机接受）。
        Return New NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances:=1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous)
    End Function

    ''' <summary>
    ''' 启动监听循环（后台任务）。同名 pipe 已被占用时抛 IOException（同 PID 重复实例：直接报错，不排队、不换名）。
    ''' </summary>
    Public Sub Start()
        If _disposed Then Throw New ObjectDisposedException(NameOf(WpfDebugPipeServer))
        If _server IsNot Nothing Then Return ' 幂等

        ' 撞名在这里暴露：同 PID 重复实例创建同名 pipe（maxNumberOfServerInstances=1）会抛 IOException。
        _server = CreateServerStream(_pipeName)
        _cts = New CancellationTokenSource()
        _listenTask = Task.Run(AddressOf ListenLoop)
    End Sub

    ''' <summary>停止监听并关闭连接。</summary>
    Public Sub [Stop]()
        If _disposed Then Return
        Try
            _cts?.Cancel()
        Catch
        End Try
        Try
            _server?.Disconnect()
        Catch
        End Try
        _server?.Dispose()
        _server = Nothing
    End Sub

    Private Async Function ListenLoop() As Task
        Dim token As CancellationToken = _cts.Token
        Dim keepLooping As Boolean = True
        While keepLooping AndAlso Not token.IsCancellationRequested
            Dim stream As NamedPipeServerStream = _server
            If stream Is Nothing Then Exit While

            Try
                Await stream.WaitForConnectionAsync(token).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                Exit While
            Catch
                ' 监听失败（如被关闭），退出循环由 Start/Stop 重建。
                Exit While
            End Try

            Dim connectionCanceled As Boolean = False
            Try
                Await ServeConnectionAsync(stream, token).ConfigureAwait(False)
            Catch ex As OperationCanceledException
                connectionCanceled = True
            Catch
                ' 单连接出错不致命，断开后等待下一个连接。
            End Try

            ' 断开当前连接（Finally 不能含 Exit/分支，故放到 Try 之外）。
            Try
                stream.Disconnect()
            Catch
            End Try

            If connectionCanceled Then Exit While

            ' 单被控 + 单实例：断开后若需再接，重建流（同一名，上一实例已释放）。
            ' 重建失败（如撞名残留）则停止循环。
            If Not token.IsCancellationRequested Then
                Try
                    _server?.Dispose()
                    _server = CreateServerStream(_pipeName)
                Catch
                    keepLooping = False
                End Try
            End If
        End While
    End Function

    ''' <summary>
    ''' 服务单个连接：先发握手，再循环收请求回响应。
    ''' </summary>
    Protected Overridable Async Function ServeConnectionAsync(stream As NamedPipeServerStream, token As CancellationToken) As Task
        ' 握手：回报 pid / 主窗口标题 / 协议版本。
        Dim handshake As HandshakeInfo = Await BuildHandshakeInfoAsync(token).ConfigureAwait(False)
        Await MessageFramer.WriteAsync(stream, handshake, token).ConfigureAwait(False)

        While Not token.IsCancellationRequested
            ' 读请求带空闲超时：对端连上但长时间不发数据时不至于永久阻塞 IPC 线程。
            ' 空闲超时只断当前连接（回到监听等下一个连接），不影响被控端存活。
            Dim request As WpfDebugRequest
            Using idleCts As CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token)
                idleCts.CancelAfter(ReadIdleTimeoutMs)
                Try
                    request = Await MessageFramer.ReadAsync(Of WpfDebugRequest)(stream, idleCts.Token).ConfigureAwait(False)
                Catch ex As OperationCanceledException
                    ' token 自身取消 → 整体退出；只是 idleCts 超时 → 断开此连接继续监听。
                    If token.IsCancellationRequested Then Exit While
                    Exit While
                End Try
            End Using

            If request Is Nothing Then Exit While ' 对端干净关闭

            Dim response As WpfDebugResponse = Await DispatchAsync(request, token).ConfigureAwait(False)
            Await MessageFramer.WriteAsync(stream, response, token).ConfigureAwait(False)
        End While
    End Function

    ''' <summary>
    ''' 读请求的空闲超时（毫秒）。对端连上后在此时间内不发任何字节则断开此连接。
    ''' 取较大值避免慢请求被误断；主控正常调用每帧都会立即发数据。
    ''' </summary>
    Protected Const ReadIdleTimeoutMs As Integer = 60000

    ''' <summary>
    ''' 构造握手信息。进程信息从非 UI 来源取（pid / 可执行路径是进程级），主窗口标题切 UI 线程读。
    ''' </summary>
    Protected Overridable Async Function BuildHandshakeInfoAsync(token As CancellationToken) As Task(Of HandshakeInfo)
        Dim title As String = Nothing
        Try
            title = Await _dispatcher.InvokeAsync(Function() As String
                                                      Dim app As Global.System.Windows.Application = Global.System.Windows.Application.Current
                                                      Dim win As Global.System.Windows.Window = If(app?.MainWindow, Nothing)
                                                      Return win?.Title
                                                  End Function).ConfigureAwait(False)
        Catch
        End Try

        ' 可执行路径：Process.MainModule.FileName 在 net472 / net8.0 均可取，但权限/位数差异可能抛，
        ' 兜底给空字符串（主控据此区分"未提供"与"显式空"，processPath 字段恒序列化）。
        Dim processPath As String = String.Empty
        Try
            processPath = Process.GetCurrentProcess().MainModule?.FileName
        Catch
        End Try
        If processPath Is Nothing Then processPath = String.Empty

        Return New HandshakeInfo With {
            .ProtocolVersion = WpfDebugProtocol.ProtocolVersion,
            .Pid = Process.GetCurrentProcess().Id,
            .MainWindowTitle = title,
            .ProcessPath = processPath
        }
    End Function

    ''' <summary>
    ''' 按 Method 分派请求到 <see cref="IWpfDebugTarget"/>。未知方法返回错误响应。
    ''' 分派体整体经 <see cref="WpfDispatcher.InvokeAsync"/> 切到 UI 线程异步执行——
    ''' 绝不在 IPC 线程上同步等 UI 线程（那是 rough-plan 第十一节 4 点名的死锁源：
    ''' 宿主 UI 线程被模态框/长任务占用时，同步 Dispatcher.Invoke 会无限等待）。
    ''' IWpfDebugTarget 的实现约定：被本方法在 UI 线程内调用，自身不再二次切线程。
    ''' </summary>
    Protected Overridable Async Function DispatchAsync(request As WpfDebugRequest, token As CancellationToken) As Task(Of WpfDebugResponse)
        Dim response As New WpfDebugResponse With {.Id = request.Id}

        ' UI 线程内执行分派；返回 (resultJObject, error)。null result 表示无结构化结果。
        Dim dispatchResult As DispatchOutcome = Nothing
        Try
            dispatchResult = Await _dispatcher.InvokeAsync(Function() DispatchOnUiThread(request)).ConfigureAwait(False)
        Catch ex As Exception
            ' Dispatcher 本身不可用或分派抛出（含 target 抛 NotImplementedException）。
            response.Error = ToError(ex, request.Method)
        End Try

        If dispatchResult?.Error IsNot Nothing Then
            response.Error = dispatchResult.Error
        ElseIf dispatchResult?.Result IsNot Nothing Then
            response.Result = dispatchResult.Result
        End If

        Return response
    End Function

    ''' <summary>分派在 UI 线程上执行，产出结果 JObject 或错误。</summary>
    Private Function DispatchOnUiThread(request As WpfDebugRequest) As DispatchOutcome
        Try
            Select Case request.Method
                Case WpfDebugMethods.ListWindows
                    Return OkResult(_target.ListWindows())
                Case WpfDebugMethods.TakeSnapshot
                    Dim windowId As String = ReadParam(Of String)(request, "windowId")
                    Dim maxDepth As Integer? = ReadNullableInt(request, "maxDepth")
                    Dim interestingOnly As Boolean = ReadParam(Of Boolean)(request, "interestingOnly", False)
                    Return OkResult(_target.TakeSnapshot(windowId, maxDepth, interestingOnly))
                Case WpfDebugMethods.Click
                    _target.Click(ReadParam(Of String)(request, "uid"))
                    Return OkResult(Nothing)
                Case WpfDebugMethods.Fill
                    _target.Fill(ReadParam(Of String)(request, "uid"), ReadParam(Of String)(request, "value"))
                    Return OkResult(Nothing)
                Case WpfDebugMethods.Evaluate
                    Dim script As String = ReadParam(Of String)(request, "script")
                    Dim timeoutMs As Integer? = ReadNullableInt(request, "timeoutMs")
                    Return OkResult(_target.Evaluate(script, timeoutMs))
                Case WpfDebugMethods.TakeScreenshot
                    Return OkResult(_target.TakeScreenshot(ReadParam(Of String)(request, "windowId"), ReadParam(Of String)(request, "uid")))
                Case WpfDebugMethods.GetProperty
                    Return OkResult(_target.GetProperty(ReadParam(Of String)(request, "uid"), ReadParam(Of String)(request, "propertyName")))
                Case WpfDebugMethods.SetProperty
                    _target.SetProperty(ReadParam(Of String)(request, "uid"), ReadParam(Of String)(request, "propertyName"), ReadParam(Of String)(request, "value"))
                    Return OkResult(Nothing)
                Case WpfDebugMethods.GetLogicalTree
                    Return OkResult(_target.GetLogicalTree(ReadParam(Of String)(request, "windowId"), ReadNullableInt(request, "maxDepth")))
                Case WpfDebugMethods.ListBindings
                    Return OkResult(_target.ListBindings(ReadParam(Of String)(request, "uid")))
                Case WpfDebugMethods.Highlight
                    _target.Highlight(ReadParam(Of String)(request, "uid"))
                    Return OkResult(Nothing)
                Case WpfDebugMethods.ListEvents
                    Return OkResult(_target.ListEvents())
                Case Else
                    Return New DispatchOutcome With {.Error = New WpfDebugError With {.Code = -32601, .Message = $"未知方法：{request.Method}"}}
            End Select
        Catch ex As Exception
            Return New DispatchOutcome With {.Error = ToError(ex, request.Method)}
        End Try
    End Function

    ' 把任意返回值包成 JObject 结果。Result 字段契约是 JObject（#17），
    ' 故把值统一放在 "result" 键下：对象/数组/标量都序列化成 JToken 挂进去，
    ' 主控侧按此约定取。无返回值（Sub 类方法）给空对象。
    Private Shared Function OkResult(value As Object) As DispatchOutcome
        Dim jo As New JObject()
        If value IsNot Nothing Then
            jo("result") = JToken.FromObject(value)
        End If
        Return New DispatchOutcome With {.Result = jo}
    End Function

    Private Shared Function ToError(ex As Exception, method As String) As WpfDebugError
        If TypeOf ex Is NotImplementedException Then
            Return New WpfDebugError With {.Code = -32603, .Message = $"方法未实现：{method}"}
        End If
        Return New WpfDebugError With {.Code = -32603, .Message = If(ex.Message, ex.GetType().Name)}
    End Function

    ' 分派产出：Result 与 Error 二选一。
    Private Class DispatchOutcome
        Public Property Result As JObject
        Public Property [Error] As WpfDebugError
    End Class

    Private Function ReadParam(Of T)(request As WpfDebugRequest, name As String, Optional defaultValue As T = Nothing) As T
        If request.Params Is Nothing Then Return defaultValue
        Dim token As JToken = request.Params(name)
        If token Is Nothing Then Return defaultValue
        Return token.Value(Of T)()
    End Function

    Private Function ReadNullableInt(request As WpfDebugRequest, name As String) As Integer?
        If request.Params Is Nothing Then Return Nothing
        Dim token As JToken = request.Params(name)
        If token Is Nothing OrElse token.Type = JTokenType.Null Then Return Nothing
        Return token.Value(Of Integer)()
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        [Stop]()
    End Sub
End Class
