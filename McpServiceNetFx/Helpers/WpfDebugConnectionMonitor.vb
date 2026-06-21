Imports System.Diagnostics
Imports System.Timers

' WPF 调试连接管理（主控侧）。
'
' 单被控端 + 固定 pipe 名，不需要多连接列表/持久化：
' - WpfDebugConnection（在 McpServiceNetFx.Core）：一次成功握手的快照（pid / 主窗口标题 / 协议版本）。
' - WpfDebugConnectionMonitor（本文件）：照搬 VisualStudioMonitor 的判活/失效事件/清理模式，
'   判活依据改为 pipe 连接状态（WpfDebugProxy.IsConnected）+ 目标 pid 是否仍存活。
'   失效时触发 ConnectionLost 事件，由 MainWindow.WpfDebug 处理清理（更新 UI、清 proxy 注入）。

''' <summary>
''' WPF 调试被控端连接监控。照搬 <see cref="VisualStudioMonitor"/> 的判活/失效事件/清理模式。
''' </summary>
''' <remarks>
''' 判活依据与 VisualStudioMonitor 不同：VisualStudioMonitor 靠窗口句柄 + 标题判 DTE 是否还在，
''' 这里靠两层判：proxy 是否仍连接（pipe 状态）+ 握手 pid 的进程是否仍存活。
''' 任一失效即触发 <see cref="ConnectionLost"/>，由 UI 层清理。
''' </remarks>
Public Class WpfDebugConnectionMonitor
    Implements IDisposable

    Private ReadOnly _proxy As WpfDebugProxy
    Private ReadOnly _connection As WpfDebugConnection
    Private _timer As Timer
    Private _isDisposed As Boolean

    ''' <summary>连接失效时触发（pipe 断开或被控进程退出）。</summary>
    Public Event ConnectionLost As EventHandler(Of EventArgs)

    Public Sub New(proxy As WpfDebugProxy, connection As WpfDebugConnection)
        If proxy Is Nothing Then Throw New ArgumentNullException(NameOf(proxy))
        If connection Is Nothing Then Throw New ArgumentNullException(NameOf(connection))
        _proxy = proxy
        _connection = connection

        ' proxy 自身的 Disconnected 事件是最快失效信号（pipe 断开立即触发）。
        AddHandler _proxy.Disconnected, AddressOf OnProxyDisconnected

        SetupMonitoringTimer()
    End Sub

    Private Sub SetupMonitoringTimer()
        _timer = New Timer(2000) ' 每 2 秒判活一次，与 VisualStudioMonitor 对齐。
        AddHandler _timer.Elapsed, AddressOf CheckConnectionStatus
        _timer.Start()
    End Sub

    Private Sub CheckConnectionStatus(sender As Object, e As ElapsedEventArgs)
        If _isDisposed Then Return

        If Not IsAlive() Then
            _timer.Stop()
            OnConnectionLost()
        End If
    End Sub

    ''' <summary>
    ''' 判活：proxy 仍连接 且 握手 pid 的进程仍存活。任一不满足即失效。
    ''' </summary>
    Public Function IsAlive() As Boolean
        Try
            If _proxy Is Nothing OrElse Not _proxy.IsConnected Then Return False

            ' 握手 pid 进程是否仍在。pid 为 0（异常握手）直接判活交给 pipe 状态。
            If _connection.Pid <= 0 Then Return True

            ' GetProcessById 找不到则抛 ArgumentException，视为进程已退出。
            Dim proc As Process = Process.GetProcessById(_connection.Pid)
            proc.Dispose()
            Return True
        Catch
            Return False
        End Try
    End Function

    Private Sub OnProxyDisconnected(sender As Object, e As EventArgs)
        ' proxy 断开（最快失效路径）：停定时器并触发事件。避免重复触发。
        If _isDisposed Then Return
        _timer?.Stop()
        OnConnectionLost()
    End Sub

    Private Sub OnConnectionLost()
        RaiseEvent ConnectionLost(Me, EventArgs.Empty)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not _isDisposed AndAlso disposing Then
            If _timer IsNot Nothing Then
                _timer.Stop()
                RemoveHandler _timer.Elapsed, AddressOf CheckConnectionStatus
                _timer.Dispose()
                _timer = Nothing
            End If

            Try
                RemoveHandler _proxy.Disconnected, AddressOf OnProxyDisconnected
            Catch
            End Try

            _isDisposed = True
        End If
    End Sub

    Protected Overrides Sub Finalize()
        Dispose(False)
    End Sub
End Class
