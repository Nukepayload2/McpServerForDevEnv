' WPF 调试 tab 的 partial 实现（#22）。
' 仿 MainWindow.VsInstances.vb / MainWindow.McpService.vb 的拆法：partial class MainWindow，
' 字段 + 事件处理。管理 WpfDebugProxy（pipe client）+ WpfDebugConnection（握手快照）+
' WpfDebugConnectionMonitor（判活/失效）。连上后给工具管理器注入 proxy（DC 注入扩展点）。
'
' 单被控端 + 固定 pipe 名：UI 极简，一个连接/断开按钮 + 当前连接状态（pid / 主窗口标题）。
' 不追求"连上才显示工具"，WPF 工具常驻注册（rough-plan 第九节 4），未连接时工具调用报"未连接被控端"。

Partial Public Class MainWindow
    ' 当前 WPF 调试 proxy（连接成功后非空，断开/失效后置空）。
    Private _wpfDebugProxy As WpfDebugProxy
    ' 当前连接快照（握手信息）。
    Private _wpfDebugConnection As WpfDebugConnection
    ' 连接监控（判活/失效）。
    Private _wpfDebugMonitor As WpfDebugConnectionMonitor

    ''' <summary>连接按钮：从候选列表取选中项 PID，异步连被控端 pipe server。</summary>
    Private Async Sub BtnWpfDebugConnect_Click() Handles BtnWpfDebugConnect.Click
        If _wpfDebugProxy IsNot Nothing AndAlso _wpfDebugProxy.IsConnected Then
            UtilityModule.ShowWarning(Me, "WPF 调试被控端已连接", "提示")
            Return
        End If

        ' 选中连接语义：必须先在候选列表选一个目标。
        Dim selected As WpfDebugTargetInfo = TryCast(DgWpfDebugTargets.SelectedItem, WpfDebugTargetInfo)
        If selected Is Nothing Then
            UtilityModule.ShowWarning(Me, "请先在列表中选择一个 WPF 调试目标（必要时先点""刷新目标""）", "提示")
            Return
        End If

        BtnWpfDebugConnect.IsEnabled = False
        Try
            Await ConnectWpfDebugAsync(selected.Pid)
        Catch ex As Exception
            ' 连接失败/握手失败：报"未连接被控端"风格，不裸崩。
            TxtWpfDebugStatus.Text = "连接失败"
            UtilityModule.ShowError(Me, $"连接 WPF 调试被控端失败：{ex.Message}")
        Finally
            BtnWpfDebugConnect.IsEnabled = True
        End Try
    End Sub

    ''' <summary>刷新目标按钮：枚举系统 named pipe，填候选列表（不连接）。</summary>
    Private Sub BtnRefreshWpfTargets_Click() Handles BtnRefreshWpfTargets.Click
        Try
            Dim candidates As IList(Of WpfDebugTargetInfo) = WpfDebugTargetEnumerator.DiscoverCandidates()
            DgWpfDebugTargets.ItemsSource = candidates
        Catch ex As Exception
            UtilityModule.ShowError(Me, $"刷新 WPF 调试目标失败：{ex.Message}")
        End Try
    End Sub

    ''' <summary>断开按钮。</summary>
    Private Sub BtnWpfDebugDisconnect_Click() Handles BtnWpfDebugDisconnect.Click
        DisconnectWpfDebug()
    End Sub

    ''' <summary>异步连接被控端、握手、建监控、注入 DC。</summary>
    ''' <param name="targetPid">用户在候选列表选中的被控进程 PID。</param>
    Private Async Function ConnectWpfDebugAsync(targetPid As Integer) As Task
        ' 清理旧状态（重复连接场景）。
        TeardownWpfDebug()

        _wpfDebugProxy = New WpfDebugProxy(targetPid)
        Await _wpfDebugProxy.ConnectAsync()

        ' 握手成功：建连接快照。
        _wpfDebugConnection = New WpfDebugConnection(_wpfDebugProxy.Handshake)

        ' 建监控：失效时清理。
        _wpfDebugMonitor = New WpfDebugConnectionMonitor(_wpfDebugProxy, _wpfDebugConnection)
        AddHandler _wpfDebugMonitor.ConnectionLost, AddressOf OnWpfDebugConnectionLost

        ' DC 注入扩展点：把 proxy 注入给所有已注册工具。
        If _toolManager IsNot Nothing Then
            _toolManager.CreateWpfDebugTools(_wpfDebugProxy, New DispatcherService(Dispatcher))
        End If

        UpdateWpfDebugUI(connected:=True)
    End Function

    ''' <summary>主动断开：用户点断开按钮。</summary>
    Private Sub DisconnectWpfDebug()
        TeardownWpfDebug()
        UpdateWpfDebugUI(connected:=False)
    End Sub

    ''' <summary>连接失效（监控触发）：UI 线程上清理 + 更新 UI。</summary>
    Private Sub OnWpfDebugConnectionLost(sender As Object, e As EventArgs)
        ' ConnectionLost 来自定时器线程或 proxy 后台循环，切回 UI 线程更新。
        Dispatcher.BeginInvoke(Sub()
                                   TeardownWpfDebug()
                                   UpdateWpfDebugUI(connected:=False)
                               End Sub)
    End Sub

    ''' <summary>
    ''' 拆除当前 WPF 调试连接：清 proxy 注入、停监控、Dispose proxy。
    ''' 幂等。
    ''' </summary>
    Private Sub TeardownWpfDebug()
        ' 先清工具的 proxy 注入（让 IsWpfDebugConnected 回到 False）。
        If _toolManager IsNot Nothing Then
            _toolManager.ClearWpfDebugProxy()
        End If

        If _wpfDebugMonitor IsNot Nothing Then
            Try
                RemoveHandler _wpfDebugMonitor.ConnectionLost, AddressOf OnWpfDebugConnectionLost
            Catch
            End Try
            _wpfDebugMonitor.Dispose()
            _wpfDebugMonitor = Nothing
        End If

        If _wpfDebugProxy IsNot Nothing Then
            _wpfDebugProxy.Disconnect()
            _wpfDebugProxy.Dispose()
            _wpfDebugProxy = Nothing
        End If

        _wpfDebugConnection = Nothing
    End Sub

    ''' <summary>更新 WPF 调试 tab 的 UI 状态。</summary>
    Private Sub UpdateWpfDebugUI(connected As Boolean)
        If connected AndAlso _wpfDebugConnection IsNot Nothing Then
            TxtWpfDebugStatus.Text = "已连接"
            TxtWpfDebugPid.Text = _wpfDebugConnection.Pid.ToString()
            ' 主窗口标题为主显示；可选附带可执行路径（握手有则显示）。
            Dim titleDisplay As String = If(_wpfDebugConnection.MainWindowTitle, "-")
            Dim procPath As String = _wpfDebugConnection.ProcessPath
            If Not String.IsNullOrEmpty(procPath) Then
                titleDisplay &= "  (" & procPath & ")"
            End If
            TxtWpfDebugTitle.Text = titleDisplay
            BtnWpfDebugConnect.IsEnabled = False
            BtnWpfDebugDisconnect.IsEnabled = True
        Else
            TxtWpfDebugStatus.Text = "未连接"
            TxtWpfDebugPid.Text = "-"
            TxtWpfDebugTitle.Text = "-"
            BtnWpfDebugConnect.IsEnabled = True
            BtnWpfDebugDisconnect.IsEnabled = False
        End If
    End Sub
End Class
