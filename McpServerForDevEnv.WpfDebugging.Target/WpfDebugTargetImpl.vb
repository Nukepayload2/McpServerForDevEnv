Imports System.Collections.Generic
Imports McpServerForDevEnv.WpfDebugging

' 被控端能力实现骨架。实现共享 Core 的 IWpfDebugTarget 契约。
' 本块（被控端骨架）只搭起结构，具体能力（可视树遍历、点击、填值、截图、脚本）留给
' 后续阶段实现（#19/#20/#21）。pipe 请求分派由 WpfDebugPipeServer 按 Method 名调用这里对应的方法。
'
' 【线程分层约定 —— 避免死锁，后续实现务必遵守】
' IWpfDebugTarget 是同步签名（Core 契约，文档化能力面）。被控端的执行链是：
'   IPC 线程收到请求 → WpfDebugPipeServer.DispatchAsync 经 WpfDispatcher.InvokeAsync
'   把分派体（含本类的方法调用）整体切到 UI 线程 → 在 UI 线程内同步调本类方法 → 回结果。
' 因此本类的方法【约定已在 UI 线程被调用】，实现时直接碰 WPF 对象即可，
' 【不得】再自调 Dispatcher.Invoke/InvokeAsync 二次切线程——那会在 UI 线程上排队等待自己，
' 是 rough-plan 第十一节 4 点名的死锁源。
' 骨架阶段各方法抛 NotImplementedException 占位，经上述链路被包成错误响应回主控。

''' <summary>
''' <see cref="IWpfDebugTarget"/> 的被控端实现（骨架）。
''' </summary>
Public Class WpfDebugTargetImpl
    Implements IWpfDebugTarget

    Private ReadOnly _dispatcher As WpfDispatcher
    Private ReadOnly _uidRegistry As UidRegistry
    Private ReadOnly _windowEnumerator As WindowEnumerator
    Private ReadOnly _treeWalker As VisualTreeWalker

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
        Throw New NotImplementedException("click 由后续阶段实现(#20)。")
    End Sub

    Public Sub Fill(uid As String, value As String) Implements IWpfDebugTarget.Fill
        Throw New NotImplementedException("fill 由后续阶段实现(#20)。")
    End Sub

    Public Function Evaluate(script As String, timeoutMs As Integer?) As String Implements IWpfDebugTarget.Evaluate
        Throw New NotImplementedException("evaluate 由后续阶段实现(#21)。")
    End Function

    Public Function TakeScreenshot(windowId As String, uid As String) As ScreenshotResult Implements IWpfDebugTarget.TakeScreenshot
        Throw New NotImplementedException("take_screenshot 由后续阶段实现(#20)。")
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
