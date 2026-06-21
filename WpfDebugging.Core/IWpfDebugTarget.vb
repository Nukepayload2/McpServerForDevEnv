' WPF 调试被控端要实现的能力接口。
' 纯托管类型签名，零 WPF / EnvDTE 强依赖。这个接口主要是契约文档化：
' 被控端实现它，主控侧代理（pipe client）用同构签名，二者靠 Method 常量一一对应。
' 方法签名与 WpfDebugMethods 里的常量一一对应，参数能从 WpfDebugRequest.Params 反序列化得到。

''' <summary>
''' WPF 调试被控端的能力契约。被控端实现此接口；主控侧代理对外暴露同构签名。
''' </summary>
''' <remarks>
''' 所有碰 WPF 对象的实现必须在宿主 UI 线程上执行（被控端用 Dispatcher 调度），
''' 但契约本身不暴露任何 WPF 类型——uid 是字符串，窗口由 windowId 引用。
''' </remarks>
Public Interface IWpfDebugTarget

    ''' <summary>列出当前被控进程的所有窗口。对应 <see cref="WpfDebugMethods.ListWindows"/>。</summary>
    Function ListWindows() As IList(Of WindowInfo)

    ''' <summary>
    ''' 拍指定窗口的可视树快照。对应 <see cref="WpfDebugMethods.TakeSnapshot"/>。
    ''' </summary>
    ''' <param name="windowId">目标窗口 id；Nothing 表示当前窗口。</param>
    ''' <param name="maxDepth">最大遍历深度；Nothing 表示不限。</param>
    ''' <param name="interestingOnly">是否裁掉纯装饰节点（对标 chrome 的 interestingOnly）。</param>
    Function TakeSnapshot(windowId As String, maxDepth As Integer?, interestingOnly As Boolean) As SnapshotNode

    ''' <summary>按 uid 点击元素。对应 <see cref="WpfDebugMethods.Click"/>。</summary>
    Sub Click(uid As String)

    ''' <summary>按 uid 给控件填值。对应 <see cref="WpfDebugMethods.Fill"/>。</summary>
    ''' <param name="uid">目标元素 uid。</param>
    ''' <param name="value">要填的值（按控件类型分流）。</param>
    Sub Fill(uid As String, value As String)

    ''' <summary>
    ''' 在被控进程上下文执行一段 VB.NET 脚本并返回结果。对应 <see cref="WpfDebugMethods.Evaluate"/>。
    ''' </summary>
    ''' <param name="script">VB.NET 脚本源。</param>
    ''' <param name="timeoutMs">执行超时（毫秒）；Nothing 表示不限。</param>
    ''' <returns>脚本结果（字符串形式，由被控端序列化）。</returns>
    Function Evaluate(script As String, timeoutMs As Integer?) As String

    ''' <summary>
    ''' 截当前窗口或指定 uid 元素的图。对应 <see cref="WpfDebugMethods.TakeScreenshot"/>。
    ''' </summary>
    ''' <param name="windowId">目标窗口 id；Nothing 表示当前窗口。</param>
    ''' <param name="uid">目标元素 uid；Nothing 表示截整个窗口。</param>
    Function TakeScreenshot(windowId As String, uid As String) As ScreenshotResult

    ''' <summary>读取依赖属性。对应 <see cref="WpfDebugMethods.GetProperty"/>。</summary>
    Function GetProperty(uid As String, propertyName As String) As String

    ''' <summary>写入依赖属性。对应 <see cref="WpfDebugMethods.SetProperty"/>。</summary>
    Sub SetProperty(uid As String, propertyName As String, value As String)

    ''' <summary>拍逻辑树快照。对应 <see cref="WpfDebugMethods.GetLogicalTree"/>。</summary>
    Function GetLogicalTree(windowId As String, maxDepth As Integer?) As SnapshotNode

    ''' <summary>列出指定元素的绑定信息。对应 <see cref="WpfDebugMethods.ListBindings"/>。</summary>
    Function ListBindings(uid As String) As IList(Of String)

    ''' <summary>高亮指定 uid 元素（Snoop 风格）。对应 <see cref="WpfDebugMethods.Highlight"/>。</summary>
    Sub Highlight(uid As String)

    ''' <summary>读被控端采集的事件流。对应 <see cref="WpfDebugMethods.ListEvents"/>。</summary>
    Function ListEvents() As IList(Of String)
End Interface
