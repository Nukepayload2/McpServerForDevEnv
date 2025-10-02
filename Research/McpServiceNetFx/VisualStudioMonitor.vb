Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Timers

Public Class VisualStudioMonitor
    Private ReadOnly _dte2 As EnvDTE80.DTE2
    Private ReadOnly _cachedHWnd As IntPtr
    Private ReadOnly _originalCaption As String
    Private _timer As Timer
    Private _isDisposed As Boolean = False

    Public Event VisualStudioExited As EventHandler
    Public Event VisualStudioShutdown As EventHandler

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function IsWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetWindowText(hWnd As IntPtr, lpString As StringBuilder, nMaxCount As Integer) As Integer
    End Function

    Public Sub New(dteInstance As EnvDTE80.DTE2)
        _dte2 = dteInstance
        _cachedHWnd = dteInstance.MainWindow.HWnd
        _originalCaption = dteInstance.MainWindow.Caption

        SetupDTEEvents()
        SetupMonitoringTimer()
    End Sub

    Private Sub SetupDTEEvents()
        Try
            ' 监听 DTE 事件
            AddHandler _dte2.Events.DTEEvents.OnBeginShutdown, AddressOf OnVisualStudioShutdown
        Catch ex As Exception
            ' 如果无法添加事件处理器，说明 DTE 可能已失效
            HandleDTEDisconnected()
        End Try
    End Sub

    Private Sub SetupMonitoringTimer()
        _timer = New Timer(2000) ' 每2秒检查一次
        AddHandler _timer.Elapsed, AddressOf CheckVisualStudioStatus
        _timer.Start()
    End Sub

    Private Sub CheckVisualStudioStatus(sender As Object, e As ElapsedEventArgs)
        If _isDisposed Then Return

        Dim isExited As Boolean = False

        ' 检查窗口句柄是否有效
        If Not IsWindow(_cachedHWnd) Then
            isExited = True
        End If

        ' 检查窗口标题是否匹配（防止句柄被重用）
        If Not isExited Then
            Try
                Dim caption As String = GetWindowTitle(_cachedHWnd)
                If String.IsNullOrEmpty(caption) OrElse Not caption.Contains("Visual Studio") Then
                    isExited = True
                End If
            Catch ex As Exception
                isExited = True
            End Try
        End If

        If isExited Then
            _timer.Stop()
            OnVisualStudioExited()
        End If
    End Sub

    Private Function GetWindowTitle(hWnd As IntPtr) As String
        Dim builder As New StringBuilder(256)
        GetWindowText(hWnd, builder, builder.Capacity)
        Return builder.ToString()
    End Function

    Private Sub OnVisualStudioShutdown()
        RaiseEvent VisualStudioShutdown(Me, EventArgs.Empty)
    End Sub

    Private Sub OnVisualStudioExited()
        RaiseEvent VisualStudioExited(Me, EventArgs.Empty)
    End Sub

    Private Sub HandleDTEDisconnected()
        ' DTE 连接已断开，可能 Visual Studio 已经退出
        _timer?.Stop()
        OnVisualStudioExited()
    End Sub

    Public ReadOnly Property DTE2 As EnvDTE80.DTE2
        Get
            Return _dte2
        End Get
    End Property

    Public Function IsAlive() As Boolean
        Try
            ' 检查 DTE 是否仍然有效
            If _dte2 Is Nothing OrElse _dte2.MainWindow Is Nothing Then
                Return False
            End If

            ' 检查窗口句柄是否仍然有效
            Return IsWindow(_cachedHWnd)
        Catch ex As Exception
            Return False
        End Try
    End Function

    Public Sub Dispose()
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not _isDisposed AndAlso disposing Then
            If _timer IsNot Nothing Then
                _timer.Stop()
                RemoveHandler _timer.Elapsed, AddressOf CheckVisualStudioStatus
                _timer.Dispose()
                _timer = Nothing
            End If

            ' 移除 DTE 事件处理器
            Try
                If _dte2 IsNot Nothing Then
                    RemoveHandler _dte2.Events.DTEEvents.OnBeginShutdown, AddressOf OnVisualStudioShutdown
                End If
            Catch ex As Exception
                ' 忽略移除事件时的错误
            End Try

            _isDisposed = True
        End If
    End Sub

    Protected Overrides Sub Finalize()
        Dispose(False)
    End Sub
End Class