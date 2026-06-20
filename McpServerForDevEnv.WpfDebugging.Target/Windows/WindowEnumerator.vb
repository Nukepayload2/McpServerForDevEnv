Imports System.Collections.Generic
Imports System.Windows
Imports System.Windows.Interop
Imports McpServerForDevEnv.WpfDebugging

' 窗口枚举：枚举 Application.Current.Windows，产出 WindowInfo 列表。
' 每个窗口登记一个稳定 uid（= 窗口对象的 GetHashCode() 字符串），作为 take_snapshot 的 windowId。
'
' 【线程约定】UI 线程被调用（见 WpfDebugTargetImpl 顶部约定），直接碰 WPF 对象，不二次切线程。

''' <summary>
''' 枚举被控进程的 WPF 窗口。
''' </summary>
Public NotInheritable Class WindowEnumerator

    Private ReadOnly _uidRegistry As UidRegistry

    Public Sub New(uidRegistry As UidRegistry)
        If uidRegistry Is Nothing Then Throw New ArgumentNullException(NameOf(uidRegistry))
        _uidRegistry = uidRegistry
    End Sub

    ''' <summary>
    ''' 枚举当前所有窗口。<paramref name="mainWindow"/> 用于判定 IsMain（通常传 Application.Current.MainWindow）。
    ''' </summary>
    Public Function Enumerate(Optional mainWindow As Window = Nothing) As IList(Of WindowInfo)
        Dim result As New List(Of WindowInfo)()
        Dim app As Application = Application.Current
        If app Is Nothing Then Return result

        If mainWindow Is Nothing Then mainWindow = app.MainWindow

        ' Application.Windows 是所有由 Application 创建/跟踪的窗口。
        For Each win As Object In app.Windows
            Dim w As Window = TryCast(win, Window)
            If w Is Nothing Then Continue For
            result.Add(BuildWindowInfo(w, mainWindow))
        Next

        ' 若 Windows 集合为空但 MainWindow 非空（某些宿主只设 MainWindow），兜底补一个。
        If result.Count = 0 AndAlso mainWindow IsNot Nothing Then
            result.Add(BuildWindowInfo(mainWindow, mainWindow))
        End If

        ' 主窗口排在最前
        result.Sort(Function(a, b) If(b.IsMain, 1, 0) - If(a.IsMain, 1, 0))
        Return result
    End Function

    Private Function BuildWindowInfo(w As Window, mainWindow As Window) As WindowInfo
        Dim uid As String = _uidRegistry.GetOrCreateUid(w)
        Dim handle As Long = TryGetHandle(w)
        Return New WindowInfo With {
            .WindowId = uid,
            .Title = If(w.Title, w.GetType().Name),
            .IsMain = (mainWindow IsNot Nothing AndAlso Object.ReferenceEquals(w, mainWindow)),
            .Handle = handle
        }
    End Function

    ' 取窗口 HWND。优先 WindowInteropHelper.Handle；未建立则从 HwndSource 取。
    Private Shared Function TryGetHandle(w As Window) As Long
        Try
            Dim helper As New WindowInteropHelper(w)
            Dim h As IntPtr = helper.Handle
            If h = IntPtr.Zero Then
                Dim src As HwndSource = TryCast(PresentationSource.FromVisual(w), HwndSource)
                If src IsNot Nothing Then h = src.Handle
            End If
            Return h.ToInt64()
        Catch
            Return 0L
        End Try
    End Function
End Class
