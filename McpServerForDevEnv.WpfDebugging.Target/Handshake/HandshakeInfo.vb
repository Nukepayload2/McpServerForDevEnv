Imports Newtonsoft.Json

' 被控端握手时回报给主控的进程信息。

''' <summary>
''' 被控端握手回报。主控连上 pipe 后收到第一帧，据此判断目标进程与协议兼容性。
''' </summary>
Public Class HandshakeInfo

    ''' <summary>IPC 协议版本（取 <c>WpfDebugProtocol.ProtocolVersion</c>）。</summary>
    <JsonProperty("protocolVersion")>
    Public Property ProtocolVersion As String

    ''' <summary>被控进程 id。</summary>
    <JsonProperty("pid")>
    Public Property Pid As Integer

    ''' <summary>被控进程主窗口标题（握手瞬间抓取，可能为空）。</summary>
    <JsonProperty("mainWindowTitle", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property MainWindowTitle As String
End Class
