Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq

' WPF 调试主控↔被控端 IPC 协议的契约定义。
' 这一层只放纯 POCO 消息类型、协议常量与 Method 名，零 WPF / EnvDTE 强依赖。
' 根命名空间 McpServerForDevEnv.WpfDebugging 由 vbproj 的 RootNamespace 提供。

''' <summary>
''' WPF 调试 IPC 协议的固定常量。
''' </summary>
Public NotInheritable Class WpfDebugProtocol

    Private Sub New()
    End Sub

    ''' <summary>
    ''' 被控端 named pipe server 监听的管道名前缀。
    ''' 名字里带协议主版本号，避免和别的版本/别的用途串扰。
    ''' 实际 pipe 名为 <see cref="GetPipeNameForPid"/> 拼出：<c>前缀.PID</c>，
    ''' 这样主控可用 <c>Directory.GetFiles("\\.\pipe\")</c> 按前缀枚举出所有候选被控端，
    ''' 支持同一机器上多个 WPF 被控进程同时挂调试模块（每个 PID 占自己的 pipe）。
    ''' </summary>
    Public Const PipeNamePrefix As String = "mcpserverfordevenv.wpfdebug.v1"

    ''' <summary>
    ''' 旧版固定 pipe 名（单被控端语义遗留）。
    ''' 保留为 <see cref="PipeNamePrefix"/> 的别名，仅为兼容现存引用（如注释、单测断言）。
    ''' 新代码应直接用 <see cref="PipeNamePrefix"/> 或 <see cref="GetPipeNameForPid"/>。
    ''' </summary>
    Public Const PipeName As String = PipeNamePrefix

    ''' <summary>
    ''' 按被控进程 PID 拼出其监听的 pipe 名：<c>PipeNamePrefix &amp; "." &amp; pid</c>。
    ''' 被控端用自身 PID 调本方法得监听名；主控枚举出候选 PID 后用本方法拼出连接名。
    ''' </summary>
    Public Shared Function GetPipeNameForPid(pid As Integer) As String
        Return PipeNamePrefix & "." & pid
    End Function

    ''' <summary>
    ''' IPC 协议版本字符串，握手时被控端回报，主控据此判断兼容性。
    ''' </summary>
    Public Const ProtocolVersion As String = "1"
End Class

''' <summary>
''' WPF 调试命令的 Method 名常量，值用 snake_case 与对外 MCP 工具名保持一致，
''' 方便 AI 在 MCP 工具名与内部 IPC 方法之间直觉对应。
''' </summary>
Public NotInheritable Class WpfDebugMethods

    Private Sub New()
    End Sub

    ''' <summary>列出当前被控进程的所有窗口。</summary>
    Public Const ListWindows As String = "list_windows"

    ''' <summary>拍指定窗口的可视树快照（文本 + 结构化）。</summary>
    Public Const TakeSnapshot As String = "take_snapshot"

    ''' <summary>按 uid 点击元素。</summary>
    Public Const Click As String = "click"

    ''' <summary>按 uid 给控件填值（TextBox/ComboBox/CheckBox/RadioButton 分流）。</summary>
    Public Const Fill As String = "fill"

    ''' <summary>在被控进程上下文执行一段 VB.NET 脚本并返回结果。</summary>
    Public Const Evaluate As String = "evaluate"

    ''' <summary>截当前窗口或指定 uid 元素的图。</summary>
    Public Const TakeScreenshot As String = "take_screenshot"

    ''' <summary>读取依赖属性。</summary>
    Public Const GetProperty As String = "get_property"

    ''' <summary>写入依赖属性。</summary>
    Public Const SetProperty As String = "set_property"

    ''' <summary>拍逻辑树快照。</summary>
    Public Const GetLogicalTree As String = "get_logical_tree"

    ''' <summary>列出指定元素的绑定信息。</summary>
    Public Const ListBindings As String = "list_bindings"

    ''' <summary>高亮指定 uid 元素（Snoop 风格）。</summary>
    Public Const Highlight As String = "highlight"

    ''' <summary>读被控端采集的事件流（异常、绑定错误、Trace）。</summary>
    Public Const ListEvents As String = "list_events"
End Class

''' <summary>
''' uid 约定：跨快照稳定的元素字符串引用。
''' </summary>
''' <remarks>
''' uid 是主控和 AI 唯一持有的元素引用形式，被控端进程内维护 uid↔对象映射。
''' 已定方案（用户拍板，勿改）：被控端实现时直接取
''' <c>DependencyObject.GetHashCode()</c> 当 uid。WPF 的 DependencyObject 重写了
''' GetHashCode，返回进程内稳定且单调自增的唯一编号，因此同一对象跨快照 uid 不变，
''' 对象被 GC 后失效。本契约库不强依赖 DependencyObject 类型——uid 在这里只是一个
''' 字符串契约，<see cref="IWpfDebugTarget"/> 的所有方法签名都只拿 uid 字符串，
''' 不引入 ConditionalWeakTable 或任何 WPF 类型。
''' </remarks>
Public NotInheritable Class UidConventions

    Private Sub New()
    End Sub
End Class

''' <summary>
''' 主控发给被控端的请求。JSON-RPC 风格。
''' </summary>
Public Class WpfDebugRequest

    ''' <summary>请求 id，与对应响应的 id 配对。字符串形式便于跨语言。</summary>
    <JsonProperty("id")>
    Public Property Id As String

    ''' <summary>方法名，取 <see cref="WpfDebugMethods"/> 里的常量。</summary>
    <JsonProperty("method")>
    Public Property Method As String

    ''' <summary>方法参数，自由 JSON。</summary>
    <JsonProperty("params", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Params As JObject
End Class

''' <summary>
''' 被控端回给主控的响应。<see cref="Result"/> 与 <see cref="Error"/> 二选一。
''' </summary>
Public Class WpfDebugResponse

    ''' <summary>对应请求的 id。</summary>
    <JsonProperty("id")>
    Public Property Id As String

    ''' <summary>成功时的结果，自由 JSON。</summary>
    <JsonProperty("result", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Result As JObject

    ''' <summary>失败时的错误信息。</summary>
    <JsonProperty("error", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property [Error] As WpfDebugError
End Class

''' <summary>
''' 被控端主动推给主控的事件（异常、绑定错误、Trace、窗口开关等）。
''' </summary>
Public Class WpfDebugEvent

    ''' <summary>事件名（如 "window_opened"、"unhandled_exception"）。</summary>
    <JsonProperty("event")>
    Public Property [Event] As String

    ''' <summary>事件数据，自由 JSON。</summary>
    <JsonProperty("data", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Data As JObject
End Class

''' <summary>
''' WPF 调试 IPC 的错误对象，结构对齐 JSON-RPC error。
''' </summary>
Public Class WpfDebugError

    ''' <summary>错误码。</summary>
    <JsonProperty("code")>
    Public Property Code As Integer

    ''' <summary>错误消息。</summary>
    <JsonProperty("message")>
    Public Property Message As String

    ''' <summary>可选的错误附加数据，自由 JSON。</summary>
    <JsonProperty("data", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Data As JObject
End Class
