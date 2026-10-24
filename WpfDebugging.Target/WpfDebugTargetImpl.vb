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
        Dim target As Global.System.Windows.DependencyObject = ResolveUidOrThrow(uid, "get_property")
        Return PropertyAccessor.GetPropertyValue(target, propertyName)
    End Function

    Public Sub SetProperty(uid As String, propertyName As String, value As String) Implements IWpfDebugTarget.SetProperty
        Dim target As Global.System.Windows.DependencyObject = ResolveUidOrThrow(uid, "set_property")
        PropertyAccessor.SetPropertyValue(target, propertyName, value)
    End Sub

    Public Function GetLogicalTree(windowId As String, maxDepth As Integer?) As SnapshotNode Implements IWpfDebugTarget.GetLogicalTree
        Dim window As Global.System.Windows.Window = ResolveWindow(windowId)
        Dim root As Global.System.Windows.DependencyObject = If(
            TryCast(window.Content, Global.System.Windows.DependencyObject),
            DirectCast(window, Global.System.Windows.DependencyObject))
        Return WalkLogical(root, 0, maxDepth)
    End Function

    ''' <summary>递归遍历逻辑树（与可视树的差别：逻辑树含未模板化/未渲染的子元素）。</summary>
    Private Function WalkLogical(node As Global.System.Windows.DependencyObject,
                                 depth As Integer,
                                 maxDepth As Integer?) As SnapshotNode
        Dim result As New SnapshotNode With {
            .Uid = _uidRegistry.GetOrCreateUid(node),
            .TypeName = node.GetType().Name
        }

        Dim fe As Global.System.Windows.FrameworkElement = TryCast(node, Global.System.Windows.FrameworkElement)
        If fe IsNot Nothing AndAlso Not String.IsNullOrEmpty(fe.Name) Then
            result.Name = fe.Name
        End If

        If maxDepth.HasValue AndAlso depth >= maxDepth.Value Then Return result

        For Each child As Object In Global.System.Windows.LogicalTreeHelper.GetChildren(node)
            Dim childDo As Global.System.Windows.DependencyObject = TryCast(child, Global.System.Windows.DependencyObject)
            If childDo IsNot Nothing Then
                If result.Children Is Nothing Then result.Children = New List(Of SnapshotNode)()
                result.Children.Add(WalkLogical(childDo, depth + 1, maxDepth))
            End If
        Next

        Return result
    End Function

    Public Function ListBindings(uid As String) As IList(Of String) Implements IWpfDebugTarget.ListBindings
        Dim target As Global.System.Windows.DependencyObject = ResolveUidOrThrow(uid, "list_bindings")

        Dim results As New List(Of String)()
        Dim seen As New HashSet(Of String)()

        ' 逐个 DP 查 BindingBase：WPF 没有「枚举当前所有绑定」的 API，
        ' 只能按类型上声明的 DP 字段去问 BindingOperations。
        Dim type As Type = target.GetType()
        While type IsNot Nothing
            For Each f As Reflection.FieldInfo In type.GetFields(
                    Reflection.BindingFlags.Public Or Reflection.BindingFlags.Static Or Reflection.BindingFlags.DeclaredOnly)

                If Not GetType(Global.System.Windows.DependencyProperty).IsAssignableFrom(f.FieldType) Then Continue For
                If Not f.Name.EndsWith("Property", StringComparison.Ordinal) Then Continue For

                Dim dp As Global.System.Windows.DependencyProperty =
                    TryCast(f.GetValue(Nothing), Global.System.Windows.DependencyProperty)
                If dp Is Nothing Then Continue For
                If Not seen.Add(dp.Name) Then Continue For

                Dim base As Global.System.Windows.Data.BindingBase =
                    Global.System.Windows.Data.BindingOperations.GetBindingBase(target, dp)
                If base Is Nothing Then Continue For

                Dim binding As Global.System.Windows.Data.Binding = TryCast(base, Global.System.Windows.Data.Binding)
                If binding IsNot Nothing Then
                    results.Add($"{dp.Name}: Binding Path={binding.Path?.Path} Mode={binding.Mode} Source={DescribeSource(binding)}")
                Else
                    results.Add($"{dp.Name}: {base.GetType().Name}")
                End If
            Next
            type = type.BaseType
        End While

        If results.Count = 0 Then results.Add("（该元素当前没有绑定）")
        Return results
    End Function

    Private Shared Function DescribeSource(binding As Global.System.Windows.Data.Binding) As String
        If binding.Source IsNot Nothing Then Return binding.Source.GetType().Name
        If Not String.IsNullOrEmpty(binding.ElementName) Then Return "ElementName=" & binding.ElementName
        If binding.RelativeSource IsNot Nothing Then Return "RelativeSource=" & binding.RelativeSource.Mode
        Return "DataContext"
    End Function

    Public Sub Highlight(uid As String) Implements IWpfDebugTarget.Highlight
        Dim target As Global.System.Windows.DependencyObject = ResolveUidOrThrow(uid, "highlight")
        Dim element As Global.System.Windows.FrameworkElement =
            TryCast(target, Global.System.Windows.FrameworkElement)
        If element Is Nothing Then
            Throw New InvalidOperationException(
                $"uid={uid}（{target.GetType().Name}）不是 FrameworkElement，无法高亮。")
        End If

        Dim layer As Global.System.Windows.Documents.AdornerLayer =
            Global.System.Windows.Documents.AdornerLayer.GetAdornerLayer(element)
        If layer Is Nothing Then
            Throw New InvalidOperationException(
                $"uid={uid}（{element.GetType().Name}）的可视祖先里没有 AdornerDecorator，无法叠加高亮层。")
        End If

        Dim adorner As New HighlightAdorner(element, Global.System.Windows.Media.Colors.Red, 2.0)
        layer.Add(adorner)

        ' 3 秒后自动移除：调试高亮不该长期留在界面上干扰观察
        Dim timer As New Global.System.Windows.Threading.DispatcherTimer With {
            .Interval = TimeSpan.FromSeconds(3)
        }
        AddHandler timer.Tick,
            Sub(sender As Object, args As EventArgs)
                timer.Stop()
                Try
                    layer.Remove(adorner)
                Catch
                    ' 元素可能在计时器到期前已从树上移除，忽略
                End Try
            End Sub
        timer.Start()
    End Sub

    ' 事件流：WPF 没有全局事件总线，这里按需在宿主主窗口上挂一组有代表性的路由事件，
    ' 首次调用 list_events 时开始采集（之前的不会补录），只保留最近 MaxRecordedEvents 条。
    Private ReadOnly _recordedEvents As New List(Of String)()
    Private _eventCaptureAttached As Boolean
    Private _eventCapturePending As Boolean
    Private Const MaxRecordedEvents As Integer = 200

    Public Function ListEvents() As IList(Of String) Implements IWpfDebugTarget.ListEvents
        EnsureEventCapture()
        Return New List(Of String)(_recordedEvents)
    End Function

    ''' <summary>
    ''' 开始采集事件。由 <see cref="WpfDebugHost.Start"/> 在宿主启动时调用，
    ''' 这样「界面上发生过什么」不会被第一次 list_events 之前的操作漏掉。
    ''' </summary>
    Public Sub BeginEventCapture()
        EnsureEventCapture()
    End Sub

    ''' <summary>挂上事件处理器；主窗口尚未创建时排一次空闲回调重试。</summary>
    Private Sub EnsureEventCapture()
        If _eventCaptureAttached Then Return

        Dim app As Global.System.Windows.Application = Global.System.Windows.Application.Current
        If app Is Nothing Then Return

        If app.MainWindow Is Nothing Then
            ' 宿主在 OnStartup 里启动调试模块时主窗口还没建出来，等空闲再试
            If Not _eventCapturePending Then
                _eventCapturePending = True
                app.Dispatcher.BeginInvoke(
                    New Action(AddressOf RetryEventCapture),
                    Global.System.Windows.Threading.DispatcherPriority.ApplicationIdle)
            End If
            Return
        End If

        _eventCaptureAttached = True
        Dim root As Global.System.Windows.UIElement = app.MainWindow

        Attach(root, Global.System.Windows.UIElement.PreviewMouseDownEvent,
               New Global.System.Windows.Input.MouseButtonEventHandler(
                   Sub(s, e) RecordEvent("PreviewMouseDown", e.OriginalSource)))
        Attach(root, Global.System.Windows.UIElement.PreviewMouseUpEvent,
               New Global.System.Windows.Input.MouseButtonEventHandler(
                   Sub(s, e) RecordEvent("PreviewMouseUp", e.OriginalSource)))
        Attach(root, Global.System.Windows.UIElement.PreviewKeyDownEvent,
               New Global.System.Windows.Input.KeyEventHandler(
                   Sub(s, e) RecordEvent("PreviewKeyDown(" & e.Key.ToString() & ")", e.OriginalSource)))
        Attach(root, Global.System.Windows.UIElement.GotFocusEvent,
               New Global.System.Windows.RoutedEventHandler(
                   Sub(s, e) RecordEvent("GotFocus", e.OriginalSource)))
    End Sub

    ''' <summary>空闲重试入口：清掉 pending 标记后再试一次。</summary>
    Private Sub RetryEventCapture()
        _eventCapturePending = False
        EnsureEventCapture()
    End Sub

    Private Shared Sub Attach(element As Global.System.Windows.UIElement,
                              routedEvent As Global.System.Windows.RoutedEvent,
                              handler As [Delegate])
        ' handledEventsToo = True：业务代码标记 Handled 的事件同样记录，用于排查点击无响应
        element.AddHandler(routedEvent, handler, True)
    End Sub

    Private Sub RecordEvent(name As String, source As Object)
        Dim element As Global.System.Windows.FrameworkElement =
            TryCast(source, Global.System.Windows.FrameworkElement)
        Dim who As String = "unknown"
        If element IsNot Nothing Then
            Dim label As String = element.GetType().Name
            If Not String.IsNullOrEmpty(element.Name) Then label &= "#" & element.Name
            who = label
        End If

        _recordedEvents.Add(String.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0:HH:mm:ss.fff} {1} @ {2}", DateTime.Now, name, who))

        ' 环形淘汰，避免长时间挂调试时无限增长
        If _recordedEvents.Count > MaxRecordedEvents Then
            _recordedEvents.RemoveRange(0, _recordedEvents.Count - MaxRecordedEvents)
        End If
    End Sub

    ''' <summary>按 uid 解析元素，失败时抛带修复指引的异常（click/fill/property 等方法共用）。</summary>
    Private Function ResolveUidOrThrow(uid As String, methodName As String) As Global.System.Windows.DependencyObject
        Dim resolved As Global.System.Windows.DependencyObject = _uidRegistry.Resolve(uid)
        If resolved Is Nothing Then
            Throw New InvalidOperationException(
                $"uid={uid} 已失效（对象被 GC 或不在当前可视树）。请重新调用 take_snapshot 获取新的 uid 再试 {methodName}。")
        End If
        Return resolved
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
