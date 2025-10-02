Partial Public Class MainWindow
    Implements IMcpLogger, IMcpPermissionHandler
    Private _mcpService As McpService
    Private _vsMonitor As VisualStudioMonitor
    Private _isServiceRunning As Boolean = False

    Private Sub LoadServiceConfig()
        Try
            Dim port = PersistenceModule.LoadServiceConfig()
            TxtPort.Text = port.ToString()
        Catch ex As Exception
            TxtPort.Text = "38080"
        End Try
    End Sub

    Private Sub BtnStartService_Click() Handles BtnStartService.Click
        If _isServiceRunning Then
            UtilityModule.ShowWarning(Me, "服务已经在运行中", "提示")
            Return
        End If

        If _selectedVsInstance Is Nothing Then
            UtilityModule.ShowWarning(Me, "请先选择一个 Visual Studio 实例", "提示")
            Return
        End If

        Dim port = UtilityModule.GetValidPort(TxtPort.Text, 8080)
        TxtPort.Text = port.ToString()

        Try
            StartMcpService(port)
        Catch ex As Exception
            UtilityModule.ShowError(Me, $"启动服务失败: {ex.Message}")
        End Try
    End Sub

    Private Sub BtnStopService_Click() Handles BtnStopService.Click
        If Not _isServiceRunning Then
            UtilityModule.ShowWarning(Me, "服务未运行", "提示")
            Return
        End If

        Try
            StopMcpService()
        Catch ex As Exception
            UtilityModule.ShowError(Me, $"停止服务失败: {ex.Message}")
        End Try
    End Sub

    Private Sub StartMcpService(port As Integer)
        Try
            ' 创建 Visual Studio 监控器
            _vsMonitor = New VisualStudioMonitor(_selectedVsInstance.DTE2)
            AddHandler _vsMonitor.VisualStudioExited, AddressOf OnVisualStudioExited
            AddHandler _vsMonitor.VisualStudioShutdown, AddressOf OnVisualStudioShutdown

            ' 创建并启动 MCP 服务
            _mcpService = New McpService(_selectedVsInstance.DTE2, port, Me, Me, Dispatcher)
            _mcpService.Start()

            ' 更新 UI 状态
            _isServiceRunning = True
            UpdateServiceUI(True)

            ' 保存配置
            PersistenceModule.SaveServiceConfig(port)

            ' 记录日志
            LogServiceAction("服务启动", "成功", $"端口: {port}, 实例: {_selectedVsInstance.Caption}")

            UtilityModule.ShowInfo(Me, $"MCP 服务已启动在端口 {port}", "服务启动成功")

        Catch ex As Exception
            CleanupService()
            Throw
        End Try
    End Sub

    Private Sub StopMcpService()
        Try
            If _mcpService IsNot Nothing Then
                _mcpService.Stop()
                _mcpService.Dispose()
                _mcpService = Nothing
            End If

            If _vsMonitor IsNot Nothing Then
                RemoveHandler _vsMonitor.VisualStudioExited, AddressOf OnVisualStudioExited
                RemoveHandler _vsMonitor.VisualStudioShutdown, AddressOf OnVisualStudioShutdown
                _vsMonitor.Dispose()
                _vsMonitor = Nothing
            End If

            ' 更新 UI 状态
            _isServiceRunning = False
            UpdateServiceUI(False)

            ' 记录日志
            LogServiceAction("服务停止", "成功", "用户手动停止")

            UtilityModule.ShowInfo(Me, "MCP 服务已停止", "服务停止")

        Catch ex As Exception
            CleanupService()
            Throw
        End Try
    End Sub

    Private Sub OnVisualStudioExited(sender As Object, e As EventArgs)
        UtilityModule.SafeBeginInvoke(Dispatcher, Sub()
                                                      LogServiceAction("Visual Studio 退出", "警告", "关联的 Visual Studio 实例已退出，服务将自动停止")
                                                      CleanupService()
                                                      UtilityModule.ShowWarning(Me, "关联的 Visual Studio 实例已退出，MCP 服务已自动停止", "实例退出")
                                                  End Sub)
    End Sub

    Private Sub OnVisualStudioShutdown(sender As Object, e As EventArgs)
        UtilityModule.SafeBeginInvoke(Dispatcher, Sub()
                                                      LogServiceAction("Visual Studio 关闭", "警告", "关联的 Visual Studio 实例正在关闭，服务将自动停止")
                                                      CleanupService()
                                                      UtilityModule.ShowWarning(Me, "关联的 Visual Studio 实例正在关闭，MCP 服务已自动停止", "实例关闭")
                                                  End Sub)
    End Sub

    Private Sub CleanupService()
        Try
            If _mcpService IsNot Nothing Then
                _mcpService.Dispose()
                _mcpService = Nothing
            End If

            If _vsMonitor IsNot Nothing Then
                RemoveHandler _vsMonitor.VisualStudioExited, AddressOf OnVisualStudioExited
                RemoveHandler _vsMonitor.VisualStudioShutdown, AddressOf OnVisualStudioShutdown
                _vsMonitor.Dispose()
                _vsMonitor = Nothing
            End If

            _isServiceRunning = False
            UpdateServiceUI(False)

        Catch ex As Exception
            ' 忽略清理时的错误
        End Try
    End Sub

    Private Sub UpdateServiceUI(isRunning As Boolean)
        If isRunning Then
            TxtServiceStatus.Text = $"服务运行中 - 端口: {TxtPort.Text}"
            BtnStartService.IsEnabled = False
            BtnStopService.IsEnabled = True
            TxtPort.IsEnabled = False
            DgVsInstances.IsEnabled = False
            BtnRefresh.IsEnabled = False
            TxtSearch.IsEnabled = False

            ' 服务启动时，显示配置选项并生成配置
            TabClientConfig.Visibility = Visibility.Visible
            GenerateMcpConfig()
        Else
            TxtServiceStatus.Text = "服务未启动"
            BtnStartService.IsEnabled = _selectedVsInstance IsNot Nothing
            BtnStopService.IsEnabled = False
            TxtPort.IsEnabled = True
            DgVsInstances.IsEnabled = True
            BtnRefresh.IsEnabled = True
            TxtSearch.IsEnabled = True

            ' 服务停止时，刷新选中的实例显示
            _selectedVsInstance = Nothing
            UpdateSelectedInstanceDisplay()
            RefreshVsInstances()

            ' 服务停止时，隐藏配置选项
            TabClientConfig.Visibility = Visibility.Collapsed
        End If
    End Sub

    Public Sub LogServiceAction(action As String, result As String, details As String) Implements IMcpLogger.LogServiceAction
        ' 使用统一的日志操作方法
        LogOperation(action, result, details)
    End Sub

    Public Sub LogMcpRequest(operation As String, result As String, details As String) Implements IMcpLogger.LogMcpRequest
        LogServiceAction($"MCP请求 - {operation}", result, details)
    End Sub

    Private Sub GenerateMcpConfig()
        If Not _isServiceRunning OrElse _selectedVsInstance Is Nothing Then
            Return
        End If

        Try
            Dim serverName = $"devenv"
            Dim port = TxtPort.Text

            ' 生成配置
            ' 生成 JSON 配置
            Dim jsonConfig As String = $"{{
  ""mcpServers"": {{
    ""{serverName}"": {{
      ""type"": ""http"",
      ""url"": ""http://localhost:{port}/mcp/""
    }}
  }}
}}"

            ' 生成 Claude CLI 配置
            Dim claudeConfig As String = $"claude mcp add --transport http visualstudio ""http://localhost:{port}/mcp/"""

            TxtJsonConfig.Text = jsonConfig
            TxtClaudeConfig.Text = claudeConfig
        Catch ex As Exception
            TxtJsonConfig.Text = $"生成配置失败: {ex.Message}"
            TxtClaudeConfig.Text = $"生成配置失败: {ex.Message}"
        End Try
    End Sub


    Protected Overrides Sub OnClosed(e As EventArgs)
        MyBase.OnClosed(e)

        ' 确保服务被正确清理
        If _isServiceRunning Then
            Try
                StopMcpService()
            Catch ex As Exception
                ' 忽略关闭时的错误
            End Try
        End If
    End Sub
End Class