Imports System.Threading.Tasks

' UI 线程调度封装。
' 死锁约束（rough-plan 第十一节 4）：被控端所有碰 WPF 对象的命令必须经
' Application.Current.Dispatcher.InvokeAsync 切到 UI 线程，且全程异步（InvokeAsync + Await），
' 绝不用同步 Dispatcher.Invoke 阻塞 IPC 线程——否则 UI 线程被宿主卡住时 IPC 线程同步等待会死锁。
' 本类只暴露异步入口，从 API 形状上杜绝同步阻塞。
'
' 注：本包根命名空间是 McpServerForDevEnv.WpfDebugging.Target，为避免与子命名空间「Windows」歧义，
' WPF 类型一律用 Global.System.Windows.* 完整限定。

''' <summary>
''' 把动作切到宿主 WPF UI 线程执行，全程异步、不阻塞调用线程。
''' </summary>
Public Class WpfDispatcher

    ''' <summary>
    ''' 在 UI 线程上执行一个返回值的函数，返回其结果的 Task。
    ''' 调用方必须 Await 这个 Task，不得 .Result/.Wait() 同步阻塞。
    ''' </summary>
    Public Overridable Function InvokeAsync(Of T)(func As Func(Of T)) As Task(Of T)
        If func Is Nothing Then Throw New ArgumentNullException(NameOf(func))
        Dim dispatcher As Global.System.Windows.Threading.Dispatcher = GetDispatcher()
        If dispatcher Is Nothing Then
            ' Application.Current 尚未建立（宿主未起 WPF Application）。回退到线程池执行，
            ' 由调用方负责保证此时 func 不碰需要 UI 线程的 WPF 对象。
            Return Task.Run(func)
        End If

        Dim operation As Global.System.Windows.Threading.DispatcherOperation(Of T) =
            dispatcher.InvokeAsync(func, Global.System.Windows.Threading.DispatcherPriority.Normal)
        Return operation.Task
    End Function

    ''' <summary>
    ''' 在 UI 线程上执行一个无返回值的动作，返回 Task。
    ''' </summary>
    Public Overridable Function InvokeAsync(action As Action) As Task
        If action Is Nothing Then Throw New ArgumentNullException(NameOf(action))
        Return InvokeAsync(Of Object)(Function()
                                         action()
                                         Return Nothing
                                     End Function)
    End Function

    ''' <summary>
    ''' 当前 WPF UI 线程的 Dispatcher；若 <c>Application.Current</c> 尚未建立则返回 Nothing。
    ''' </summary>
    Public Overridable Function GetDispatcher() As Global.System.Windows.Threading.Dispatcher
        Dim app As Global.System.Windows.Application = Global.System.Windows.Application.Current
        If app Is Nothing Then Return Nothing
        Return app.Dispatcher
    End Function
End Class
