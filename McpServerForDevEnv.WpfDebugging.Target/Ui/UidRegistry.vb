' uid 注册表：跨快照稳定的元素字符串引用 ↔ DependencyObject 的双向映射。
'
' uid 方案（用户拍板，勿改）：直接取 DependencyObject.GetHashCode() 当 uid。
' WPF 的 DependencyObject 重写了 GetHashCode，返回进程内稳定且单调自增的唯一编号，
' 因此同一对象跨快照 uid 不变，对象被 GC 后失效。主控和 AI 只持有 uid 字符串。
'
' 本类是被控端进程内的实现细节，不属于共享 Core 契约。线程安全：所有公开方法内部加锁，
' 可在任意线程调用；但实际碰 DependencyObject 的调用方仍应在 UI 线程上做（见 WpfDispatcher）。
'
' 注：本包根命名空间是 McpServerForDevEnv.WpfDebugging.Target，为避免与子命名空间「Windows」
' 歧义，WPF 类型一律用 Global.System.Windows.* 完整限定。

''' <summary>
''' 维护 uid（= <see cref="Global.System.Windows.DependencyObject.GetHashCode()"/> 的字符串形式）与元素的双向映射。
''' </summary>
Public Class UidRegistry

    ' 正向：hash code → 元素的弱引用。对象被 GC 后弱引用自动失效。
    Private ReadOnly _byHashCode As New Dictionary(Of Integer, WeakReference(Of Global.System.Windows.DependencyObject))()

    ' 反向：弱引用链路失效后的兜底，按 uid 字符串反查。
    ' 因 hash code 本身就是 uid，反查等价于查 _byHashCode，这里不重复存。
    Private ReadOnly _lock As New Object()

    ''' <summary>
    ''' 取得或登记一个元素的 uid。同一对象跨调用返回同一 uid。
    ''' </summary>
    Public Function GetOrCreateUid(element As Global.System.Windows.DependencyObject) As String
        If element Is Nothing Then Throw New ArgumentNullException(NameOf(element))
        Dim hc As Integer = element.GetHashCode()
        SyncLock _lock
            _byHashCode(hc) = New WeakReference(Of Global.System.Windows.DependencyObject)(element, trackResurrection:=False)
        End SyncLock
        Return hc.ToString(Globalization.CultureInfo.InvariantCulture)
    End Function

    ''' <summary>
    ''' 按 uid 字符串反查元素。找不到、已 GC 或弱引用失效返回 Nothing。
    ''' </summary>
    Public Function Resolve(uid As String) As Global.System.Windows.DependencyObject
        If String.IsNullOrEmpty(uid) Then Return Nothing
        Dim hc As Integer
        If Not Integer.TryParse(uid, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, hc) Then
            Return Nothing
        End If

        SyncLock _lock
            Dim wr As WeakReference(Of Global.System.Windows.DependencyObject) = Nothing
            If Not _byHashCode.TryGetValue(hc, wr) OrElse wr Is Nothing Then
                Return Nothing
            End If

            Dim element As Global.System.Windows.DependencyObject = Nothing
            If Not wr.TryGetTarget(element) OrElse element Is Nothing Then
                ' 对象已 GC，清理失效项。
                _byHashCode.Remove(hc)
                Return Nothing
            End If
            Return element
        End SyncLock
    End Function

    ''' <summary>
    ''' 清理所有弱引用已失效的条目。可选维护，避免字典长期膨胀。
    ''' </summary>
    Public Sub Scavenge()
        SyncLock _lock
            Dim dead As New List(Of Integer)
            For Each kv As KeyValuePair(Of Integer, WeakReference(Of Global.System.Windows.DependencyObject)) In _byHashCode
                Dim target As Global.System.Windows.DependencyObject = Nothing
                If kv.Value Is Nothing OrElse Not kv.Value.TryGetTarget(target) OrElse target Is Nothing Then
                    dead.Add(kv.Key)
                End If
            Next
            For Each hc As Integer In dead
                _byHashCode.Remove(hc)
            Next
        End SyncLock
    End Sub

    ''' <summary>
    ''' 当前登记条目数（含可能已失效的，仅用于诊断）。
    ''' </summary>
    Public ReadOnly Property Count As Integer
        Get
            SyncLock _lock
                Return _byHashCode.Count
            End SyncLock
        End Get
    End Property

    ''' <summary>
    ''' 清空所有映射（宿主退出时调用）。
    ''' </summary>
    Public Sub Clear()
        SyncLock _lock
            _byHashCode.Clear()
        End SyncLock
    End Sub
End Class
