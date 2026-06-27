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

    ''' <summary>
    ''' 被控进程可执行文件全路径（握手时取 <c>Process.GetCurrentProcess().MainModule.FileName</c>）。
    ''' 主控枚举/选择目标用；拿不到（权限/位数差异）时为空字符串。握手时永远序列化此字段（哪怕空），
    ''' 让主控能区分"字段缺失"与"显式空"。
    ''' </summary>
    <JsonProperty("processPath")>
    Public Property ProcessPath As String
End Class
