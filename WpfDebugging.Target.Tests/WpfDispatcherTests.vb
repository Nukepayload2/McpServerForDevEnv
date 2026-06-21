Imports System
Imports System.Threading.Tasks
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServerForDevEnv.WpfDebugging.Target

' WpfDispatcher 在「无 WPF Application」回退路径下的测试。
' 测试进程不启动 WPF Application，Application.Current 为 null，故 InvokeAsync 走 Task.Run 回退。
' 这条路径纯内存（线程池跑委托），不连 pipe、不写文件、不碰 UI 线程，无副作用。
' （真实 Dispatcher 路径需要 WPF Application + UI 线程，留给端到端验证 #24。）

<TestClass>
Public Class WpfDispatcherTests

    <TestMethod>
    Public Sub GetDispatcher_ReturnsNull_WhenNoApplication()
        ' 测试进程未起 WPF Application。
        Dim d As New WpfDispatcher()
        Assert.IsNull(d.GetDispatcher())
    End Sub

    <TestMethod>
    Public Async Function InvokeAsync_Fallback_RunsAndReturnsValue() As Task
        Dim d As New WpfDispatcher()
        Dim result As Integer = Await d.InvokeAsync(Of Integer)(Function() 42)
        Assert.AreEqual(42, result)
    End Function

    <TestMethod>
    Public Async Function InvokeAsync_Action_Fallback_Completes() As Task
        Dim d As New WpfDispatcher()
        Dim hit As Boolean = False
        Await d.InvokeAsync(New Action(Sub() hit = True))
        Assert.IsTrue(hit)
    End Function

    <TestMethod>
    Public Sub InvokeAsync_NullFunc_Throws()
        Dim d As New WpfDispatcher()
        Assert.ThrowsException(Of ArgumentNullException)(Function() d.InvokeAsync(Of Integer)(Nothing))
    End Sub

End Class
