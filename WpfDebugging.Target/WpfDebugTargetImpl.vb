Imports System.Collections.Generic
Imports McpServerForDevEnv.WpfDebugging

' 被控端能力实现。实现共享 Core 的 IWpfDebugTarget 契约。
' 已实现：list_windows / take_snapshot（#19）、click / fill / take_screenshot（#20）、
'         evaluate（#21，Roslyn VB 脚本引擎）。
' 待实现：get_property / set_property / get_logical_tree / list_bindings / highlight / list_events（阶段二）。
' pipe 请求分派由 WpfDebugPipeServer 按 Method 名调用这里对应的方法。
'
' 【线程分层约定 —— 避免死锁，后续实现务必遵守】
' IWpfDebugTarget 是同步签名（Core 契约，文档化能力面）。被控端的执行链是：
'   IPC 线程收到请求 → WpfDebugPipeServer.DispatchAsync 经 WpfDispatcher.InvokeAsync
'   把分派体（含本类的方法调用）整体切到 UI 线程 → 在 UI 线程内同步调本类方法 → 回结果。
' 因此本类的方法【约定已在 UI 线程被调用】，实现时直接碰 WPF 对象即可，
' 【不得】再自调 Dispatcher.Invoke/InvokeAsync 二次切线程——那会在 UI 线程上排队等待自己，
' 是 rough-plan 第十一节 4 点名的死锁源。
'
' 【OkResult 嵌套约定】本类方法体只返回 SnapshotNode/WindowInfo/ScreenshotResult 等业务对象或抛异常；
' 返回值封装（包成 {"result":<JToken>}）由 WpfDebugPipeServer.DispatchOnUiThread 的 OkResult 做。
' 主控侧取响应须按 response.Result["result"] 嵌套读取。方法体不碰封装。
'
' 【evaluate 线程/超时特殊说明】evaluate 方法体在 UI 线程被调用后，
' 编译/加载/执行全部同步在 UI 线程做（脚本碰元素天然安全）。超时为软超时（host 方法调用时检查）；
' 编译阶段不计入超时。极端死循环由 IPC 层 ReadIdleTimeoutMs 断连兜底。详见 ScriptEngine 注释。

''' <summary>
''' <see cref="IWpfDebugTarget"/> 的被控端实现（骨架）。
''' </summary>
Public Class WpfDebugTargetImpl
    Implements IWpfDebugTarget

    Private ReadOnly _dispatcher As WpfDispatcher
    Private ReadOnly _uidRegistry As UidRegistry
    Private ReadOnly _windowEnumerator As WindowEnumerator
    Private ReadOnly _treeWalker As VisualTreeWalker

    ' 脚本引擎懒加载（rough-plan 第十一节 3）：Roslyn 体积大（几十 MB），
    ' 不调 evaluate 的被控端进程不应背启动成本。用 Lazy(Of ScriptEngine)，
    ' 仅在第一次 Evaluate 调用时构造（进而触发 Roslyn 程序集首次加载）。
    Private ReadOnly _scriptEngine As New Lazy(Of ScriptEngine)(Function() New ScriptEngine(_uidRegistry))

    Public Sub New(dispatcher As WpfDispatcher, uidRegistry As UidRegistry)
        If dispatcher Is Nothing Then Throw New ArgumentNullException(NameOf(dispatcher))
        If uidRegistry Is Nothing Then Throw New ArgumentNullException(NameOf(uidRegistry))
        _dispatcher = dispatcher
        _uidRegistry = uidRegistry
        _windowEnumerator = New WindowEnumerator(uidRegistry)
        _treeWalker = New VisualTreeWalker(uidRegistry)
    End Sub

    Public ReadOnly Property UidRegistry As UidRegistry
        Get
            Return _uidRegistry
        End Get
    End Property

    Public ReadOnly Property Dispatcher As WpfDispatcher
        Get
            Return _dispatcher
        End Get
    End Property

    Public Function ListWindows() As IList(Of WindowInfo) Implements IWpfDebugTarget.ListWindows
        Return _windowEnumerator.Enumerate()
    End Function

    Public Function TakeSnapshot(windowId As String, maxDepth As Integer?, interestingOnly As Boolean) As SnapshotNode Implements IWpfDebugTarget.TakeSnapshot
        Dim window As Global.System.Windows.Window = ResolveWindow(windowId)
        Dim root As Global.System.Windows.DependencyObject = If(
            TryCast(window.Content, Global.System.Windows.DependencyObject),
            DirectCast(window, Global.System.Windows.DependencyObject))
        Return _treeWalker.Walk(root, maxDepth, interestingOnly)
    End Function

    Public Sub Click(uid As String) Implements IWpfDebugTarget.Click
        ' uid 时机：先 Resolve。失效（对象 GC/不在树）抛错，提示重新 take_snapshot。
        Dim resolved As Global.System.Windows.DependencyObject = _uidRegistry.Resolve(uid)
        If resolved Is Nothing Then
            Throw New InvalidOperationException(
                $"uid={uid} 已失效（对象被 GC 或不在当前可视树）。请重新调用 take_snapshot 获取新的 uid。")
        End If
        Dim element As Global.System.Windows.UIElement = TryCast(resolved, Global.System.Windows.UIElement)
        If element Is Nothing Then
            Throw New InvalidOperationException(
                $"uid={uid}（{resolved.GetType().Name}）不是 UIElement，无法 click。请重新调用 take_snapshot 确认目标。")
        End If
        ElementInteractor.Click(element)
    End Sub

    Public Sub Fill(uid As String, value As String) Implements IWpfDebugTarget.Fill
        ' uid 时机同 click。fill 进一步要求目标是 FrameworkElement（可填值控件都派生自此）。
        Dim resolved As Global.System.Windows.DependencyObject = _uidRegistry.Resolve(uid)
        If resolved Is Nothing Then
            Throw New InvalidOperationException(
                $"uid={uid} 已失效（对象被 GC 或不在当前可视树）。请重新调用 take_snapshot 获取新的 uid。")
        End If
        Dim element As Global.System.Windows.UIElement = TryCast(resolved, Global.System.Windows.UIElement)
        If element Is Nothing Then
            Throw New InvalidOperationException(
                $"uid={uid}（{resolved.GetType().Name}）不是 UIElement，无法 fill。请重新调用 take_snapshot 确认目标。")
        End If
        ElementInteractor.Fill(element, value)
    End Sub

    Public Function Evaluate(script As String, timeoutMs As Integer?) As String Implements IWpfDebugTarget.Evaluate
        ' 链路：ScriptEngine.Execute（编译→内存 PE→加载→执行入口点→序列化结果）。
        ' 所有编译/执行/超时异常由 Execute 内部捕获并序列化成 JSON 结果字符串返回，
        ' 不裸抛到 pipe 分派层。OkResult 在外层包成 {"result":<JToken>}（嵌套约定见顶部注释）。
        ' 返回字符串形如 {"success":true,"value":"..."} / {"success":false,"error":"...","errorType":"..."}。
        Return _scriptEngine.Value.Execute(script, timeoutMs)
    End Function

    Public Function TakeScreenshot(windowId As String, uid As String) As ScreenshotResult Implements IWpfDebugTarget.TakeScreenshot
        ' windowId=Nothing → 活动窗口（复用 ResolveWindow 风格解析）；非空 → 按 uid 反查窗口。
        ' uid=Nothing → 截整个窗口；非空 → 截该元素区域。
        Dim window As Global.System.Windows.Window = ResolveWindow(windowId)

        If String.IsNullOrEmpty(uid) Then
            Return WindowCapturer.CaptureWindow(window)
        End If

        Dim resolved As Global.System.Windows.DependencyObject = _uidRegistry.Resolve(uid)
        If resolved Is Nothing Then
            Throw New InvalidOperationException(
                $"uid={uid} 已失效（对象被 GC 或不在当前可视树）。请重新调用 take_snapshot 获取新的 uid。")
        End If
        Dim element As Global.System.Windows.FrameworkElement = TryCast(resolved, Global.System.Windows.FrameworkElement)
        If element Is Nothing Then
            Throw New InvalidOperationException(
                $"uid={uid}（{resolved.GetType().Name}）不是 FrameworkElement，无法截图。请重新调用 take_snapshot 确认目标。")
        End If
        Return WindowCapturer.CaptureElement(element)
    End Function

    Public Function GetProperty(uid As String, propertyName As String) As String Implements IWpfDebugTarget.GetProperty
        Throw New NotImplementedException("get_property 由后续阶段实现。")
    End Function

    Public Sub SetProperty(uid As String, propertyName As String, value As String) Implements IWpfDebugTarget.SetProperty
        Throw New NotImplementedException("set_property 由后续阶段实现。")
    End Sub

    Public Function GetLogicalTree(windowId As String, maxDepth As Integer?) As SnapshotNode Implements IWpfDebugTarget.GetLogicalTree
        Throw New NotImplementedException("get_logical_tree 由后续阶段实现。")
    End Function

    Public Function ListBindings(uid As String) As IList(Of String) Implements IWpfDebugTarget.ListBindings
        Throw New NotImplementedException("list_bindings 由后续阶段实现。")
    End Function

    Public Sub Highlight(uid As String) Implements IWpfDebugTarget.Highlight
        Throw New NotImplementedException("highlight 由后续阶段实现。")
    End Sub

    Public Function ListEvents() As IList(Of String) Implements IWpfDebugTarget.ListEvents
        Throw New NotImplementedException("list_events 由后续阶段实现。")
    End Function

    ' ===== list_windows / take_snapshot 辅助 =====

    ''' <summary>
    ''' 按 windowId 解析窗口。windowId=Nothing 取当前窗口（主窗口或第一个可见窗口）；
    ''' 非空但取不到（已关闭/GC/uid 失效）抛明确异常，由 DispatchAsync 包成错误响应回主控，
    ''' 提示重新 list_windows（对标 chrome 元素失效的处理）。
    ''' </summary>
    Private Function ResolveWindow(windowId As String) As Global.System.Windows.Window
        Dim app As Global.System.Windows.Application = Global.System.Windows.Application.Current
        If app Is Nothing Then
            Throw New InvalidOperationException("WPF Application 尚未启动，没有可枚举的窗口。")
        End If

        If String.IsNullOrEmpty(windowId) Then
            ' 当前窗口：主窗口优先，否则第一个可见窗口。
            Dim main As Global.System.Windows.Window = app.MainWindow
            If main IsNot Nothing Then Return main
            For Each obj As Object In app.Windows
                Dim w As Global.System.Windows.Window = TryCast(obj, Global.System.Windows.Window)
                If w IsNot Nothing AndAlso w.IsVisible Then Return w
            Next
            Throw New InvalidOperationException("当前没有可见窗口。")
        End If

        ' 按 uid 反查；uid 失效或对象已不是 Window 抛错。
        Dim resolved As Global.System.Windows.DependencyObject = _uidRegistry.Resolve(windowId)
        If resolved Is Nothing Then
            Throw New InvalidOperationException($"windowId={windowId} 已失效（窗口已关闭或被 GC）。请重新调用 list_windows 获取新的 windowId。")
        End If
        Dim win As Global.System.Windows.Window = TryCast(resolved, Global.System.Windows.Window)
        If win Is Nothing Then
            Throw New InvalidOperationException($"windowId={windowId} 不指向窗口对象。请重新调用 list_windows。")
        End If
        Return win
    End Function
End Class
