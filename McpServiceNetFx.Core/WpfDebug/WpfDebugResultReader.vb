Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

' WPF 调试响应解析辅助。
'
' 【关键约定 —— OkResult 嵌套】
' 被控端 WpfDebugPipeServer.OkResult 把每个方法的业务返回值统一包成
'   Result = { "result": <JToken> }
' 即响应的 WpfDebugResponse.Result 这个 JObject 内部又有一层 "result" 键，
' 真正的业务返回值挂在这一层下面。这是 handoff 点名的易错点：
' 主控侧取业务返回值必须按 response.Result("result") 嵌套取，不能直接把
' response.Result 当业务值用。本类把这条解析逻辑抽成纯函数，单测覆盖。
'
' 之所以被控端要套两层：WpfDebugResponse.Result 契约是 JObject（自由 JSON 对象），
' 而业务返回值可能是标量、数组、对象，统一塞进 "result" 键后无论什么类型都能装进 JObject，
' 主控侧取 JToken 再按需转。

''' <summary>
''' 把 <see cref="WpfDebugResponse"/> 解析成业务返回值 JToken 的纯函数集合。
''' 全部无副作用，可单测。
''' </summary>
Public NotInheritable Class WpfDebugResultReader

    Private Sub New()
    End Sub

    ''' <summary>
    ''' 从成功响应里取业务返回值。被控端 OkResult 约定：业务值挂在
    ''' <c>response.Result("result")</c> 下（嵌套一层）。
    ''' </summary>
    ''' <param name="response">被控端回的成功响应（Error 为空，Result 非空）。</param>
    ''' <returns>业务返回值 JToken；无业务返回值（Sub 类方法，空 OkResult）时返回 Nothing。</returns>
    ''' <exception cref="InvalidOperationException">
    ''' 响应里既没有 Result 也没有 Error，或 Result 不是 JObject（不符合 OkResult 约定）。
    ''' </exception>
    Public Shared Function GetPayload(response As WpfDebugResponse) As JToken
        If response Is Nothing Then Throw New ArgumentNullException(NameOf(response))

        ' 优先处理错误：被控端回了 Error 就不该当成功解析。
        If response.Error IsNot Nothing Then
            Throw New InvalidOperationException(
                $"WPF 调试请求失败：[{response.Error.Code}] {response.Error.Message}")
        End If

        Dim outer As JObject = response.Result
        If outer Is Nothing Then
            ' 协议异常：成功响应必须有 Result（被控端 OkResult 永远产 JObject，哪怕空对象）。
            Throw New InvalidOperationException("WPF 调试响应缺少 Result 字段")
        End If

        ' 嵌套取 "result" 键。被控端 OkResult 对 Sub 类方法只产空 JObject（无 "result" 键），
        ' 此时业务返回值视为 Nothing。
        Dim payload As JToken = outer("result")
        Return payload
    End Function

    ''' <summary>
    ''' 取业务返回值并强转为指定类型。取不到或类型不符返回 <paramref name="defaultValue"/>。
    ''' 用于业务返回值是标量/对象的场景。
    ''' </summary>
    Public Shared Function GetPayloadAs(Of T)(response As WpfDebugResponse, defaultValue As T) As T
        Dim payload As JToken = GetPayload(response)
        If payload Is Nothing Then Return defaultValue
        Try
            Return payload.Value(Of T)()
        Catch
            Return defaultValue
        End Try
    End Function

    ''' <summary>
    ''' 把握手帧（被控端连接后第一帧发的 JObject）解析成 pid / 主窗口标题 / 协议版本 / 可执行路径。
    ''' 握手帧不是 WpfDebugResponse，是直接的 JObject（字段：protocolVersion / pid / mainWindowTitle / processPath），
    ''' 与 OkResult 的嵌套无关。
    ''' </summary>
    Public Shared Function ParseHandshake(frame As JObject) As WpfDebugHandshakeInfo
        If frame Is Nothing Then Throw New ArgumentNullException(NameOf(frame))

        Dim version As String = Nothing
        Dim versionToken As JToken = frame("protocolVersion")
        If versionToken IsNot Nothing Then version = versionToken.Value(Of String)()

        Dim pid As Integer = 0
        Dim pidToken As JToken = frame("pid")
        If pidToken IsNot Nothing Then pid = pidToken.Value(Of Integer)()

        Dim title As String = Nothing
        Dim titleToken As JToken = frame("mainWindowTitle")
        If titleToken IsNot Nothing Then title = titleToken.Value(Of String)()

        Dim processPath As String = Nothing
        Dim pathToken As JToken = frame("processPath")
        If pathToken IsNot Nothing Then processPath = pathToken.Value(Of String)()

        Return New WpfDebugHandshakeInfo With {
            .ProtocolVersion = version,
            .Pid = pid,
            .MainWindowTitle = title,
            .ProcessPath = processPath
        }
    End Function
End Class

''' <summary>
''' WPF 调试握手信息（被控端连接后第一帧回报）。主控侧镜像类型，
''' 与被控端 HandshakeInfo 字段一一对应，靠 JSON 字段名（protocolVersion/pid/mainWindowTitle/processPath）反序列化。
''' </summary>
Public Class WpfDebugHandshakeInfo

    ''' <summary>被控端 IPC 协议版本（与 <see cref="WpfDebugProtocol.ProtocolVersion"/> 比对判断兼容性）。</summary>
    Public Property ProtocolVersion As String

    ''' <summary>被控进程 id，主控用它做判活（进程是否还在）。</summary>
    Public Property Pid As Integer

    ''' <summary>被控进程主窗口标题（握手瞬间抓取，可能为空）。</summary>
    Public Property MainWindowTitle As String

    ''' <summary>被控进程可执行文件全路径（握手时回报，拿不到时为空字符串或 Nothing）。</summary>
    Public Property ProcessPath As String
End Class
