Imports Newtonsoft.Json

' WPF 调试快照与窗口、截图的数据模型。纯 POCO，可被 Newtonsoft.Json 序列化。

''' <summary>
''' 可视树/逻辑树快照里的一个节点。
''' </summary>
''' <remarks>
''' 对标 chrome-devtools-mcp 的 a11y snapshot 节点。uid 跨快照稳定（见
''' <see cref="UidConventions"/>），主控和 AI 只持有 uid 字符串。
''' </remarks>
Public Class SnapshotNode

    ''' <summary>元素 uid，跨快照稳定。</summary>
    <JsonProperty("uid")>
    Public Property Uid As String

    ''' <summary>控件类型名（GetType().Name，如 Button、TextBox）。</summary>
    <JsonProperty("typeName")>
    Public Property TypeName As String

    ''' <summary>元素名称（x:Name / Name / Content / Text 摘要）。</summary>
    <JsonProperty("name", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Name As String

    ''' <summary>挑出的有调试价值的属性键值对（IsEnabled、Visibility、绑定摘要等）。</summary>
    <JsonProperty("properties", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Properties As IDictionary(Of String, String)

    ''' <summary>子节点。</summary>
    <JsonProperty("children", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Children As IList(Of SnapshotNode)
End Class

''' <summary>
''' 被控进程里一个窗口的概要信息。
''' </summary>
Public Class WindowInfo

    ''' <summary>窗口 id（uid 形式，跨快照稳定，对应 <see cref="UidConventions"/>）。</summary>
    <JsonProperty("windowId")>
    Public Property WindowId As String

    ''' <summary>窗口标题。</summary>
    <JsonProperty("title", NullValueHandling:=NullValueHandling.Ignore)>
    Public Property Title As String

    ''' <summary>是否为主窗口。</summary>
    <JsonProperty("isMain")>
    Public Property IsMain As Boolean

    ''' <summary>窗口句柄（HWND），用于诊断与判活。</summary>
    <JsonProperty("handle")>
    Public Property Handle As Long
End Class

''' <summary>
''' 截图结果。先以 base64 字符串承载 PNG，后续可换二进制帧。
''' </summary>
Public Class ScreenshotResult

    ''' <summary>图像宽度（像素）。</summary>
    <JsonProperty("width")>
    Public Property Width As Integer

    ''' <summary>图像高度（像素）。</summary>
    <JsonProperty("height")>
    Public Property Height As Integer

    ''' <summary>base64 编码的 PNG 数据。</summary>
    <JsonProperty("pngBase64")>
    Public Property PngBase64 As String
End Class
