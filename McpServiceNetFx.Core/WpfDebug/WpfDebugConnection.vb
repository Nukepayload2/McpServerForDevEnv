' WPF 调试连接快照（主控侧）。
'
' 一次成功握手后，被控端回报的 pid / 主窗口标题 / 协议版本的不可变容器。
' 放在 Core（而非主控 UI 项目），因为它是纯 POCO、零 UI 依赖、零外部依赖，
' 便于单测构造（Tests 项目引用 Core 即可测，无需引用 WPF UI 项目）。
'
' 单被控端 + 固定 pipe 名：同一时刻主控只持有一个 WpfDebugConnection，不需要列表/持久化。

''' <summary>
''' 一次 WPF 调试连接的握手信息快照。握手成功后由 <see cref="WpfDebugProxy.Handshake"/> 构造。
''' </summary>
Public Class WpfDebugConnection

    Private ReadOnly _handshakeInfo As WpfDebugHandshakeInfo

    Public Sub New(handshakeInfo As WpfDebugHandshakeInfo)
        If handshakeInfo Is Nothing Then Throw New ArgumentNullException(NameOf(handshakeInfo))
        _handshakeInfo = handshakeInfo
    End Sub

    ''' <summary>被控进程 id（判活用：监控检查该 pid 进程是否仍存活）。</summary>
    Public ReadOnly Property Pid As Integer
        Get
            Return _handshakeInfo.Pid
        End Get
    End Property

    ''' <summary>被控进程主窗口标题（握手瞬间抓取，可能为空——被控端窗口可能还没建好）。</summary>
    Public ReadOnly Property MainWindowTitle As String
        Get
            Return _handshakeInfo.MainWindowTitle
        End Get
    End Property

    ''' <summary>被控端 IPC 协议版本（与 WpfDebugProtocol.ProtocolVersion 比对判断兼容性）。</summary>
    Public ReadOnly Property ProtocolVersion As String
        Get
            Return _handshakeInfo.ProtocolVersion
        End Get
    End Property
End Class
