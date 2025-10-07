Imports System.ServiceModel
Imports System.Windows.Threading

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
            UtilityModule.ShowWarning(Me, My.Resources.MsgServiceAlreadyRunning, My.Resources.TitleHint)
            Return
        End If

        If _selectedVsInstance Is Nothing Then
            UtilityModule.ShowWarning(Me, My.Resources.MsgSelectVsInstance, My.Resources.TitleHint)
            Return
        End If

        Dim port = UtilityModule.GetValidPort(TxtPort.Text, 8080)
        TxtPort.Text = port.ToString()

        Try
            StartMcpService(port)
        Catch ex As Exception
            If TypeOf ex IsNot AddressAccessDeniedException Then
                UtilityModule.ShowError(Me, String.Format(My.Resources.MsgStartServiceFailed, ex.Message))
            End If
        End Try
    End Sub

    Private Sub BtnStopService_Click() Handles BtnStopService.Click
        If Not _isServiceRunning Then
            UtilityModule.ShowWarning(Me, My.Resources.MsgServiceNotRunning, My.Resources.TitleHint)
            Return
        End If

        Try
            StopMcpService()
        Catch ex As Exception
            UtilityModule.ShowError(Me, String.Format(My.Resources.MsgStopServiceFailed, ex.Message))
        End Try
    End Sub

    Private Class ClipboardService
        Implements IClipboard

        Public Sub SetText(text As String) Implements IClipboard.SetText
            Clipboard.SetText(text)
        End Sub
    End Class

    Private Class InteractionService
        Implements IInteraction

        Public Sub ShowCopyCommandDialog(title As String, message As String, command As String) Implements IInteraction.ShowCopyCommandDialog
            Dim wnd As New UserMessageWindow(title, message, command)
            wnd.ShowDialog()
        End Sub
    End Class

    Private Class DispatcherService
        Implements IDispatcher

        Private ReadOnly _dispatcher As Dispatcher

        Public Sub New(dispatcher As Dispatcher)
            _dispatcher = dispatcher
        End Sub

        Public Sub Invoke(job As Action) Implements IDispatcher.Invoke
            _dispatcher.Invoke(job)
        End Sub

        Public Async Function InvokeAsync(job As Func(Of Task)) As Task Implements IDispatcher.InvokeAsync
            Dim tcs As New TaskCompletionSource(Of Boolean)
            Dim unused = _dispatcher.BeginInvoke(
            Async Sub()
                Try
                    Await job()
                    tcs.SetResult(True)
                Catch ex As Exception
                    tcs.SetException(ex)
                End Try
            End Sub)
            Await tcs.Task
        End Function

        Public Async Function InvokeAsync(job As Action) As Task Implements IDispatcher.InvokeAsync
            Await _dispatcher.BeginInvoke(job)
        End Function
    End Class

    Private Sub StartMcpService(port As Integer)
        Try
            ' 创建 Visual Studio 监控器
            _vsMonitor = New VisualStudioMonitor(_selectedVsInstance.DTE2)
            AddHandler _vsMonitor.VisualStudioExited, AddressOf OnVisualStudioExited
            AddHandler _vsMonitor.VisualStudioShutdown, AddressOf OnVisualStudioShutdown

            ' 验证工具管理器已创建并初始化
            If _toolManager Is Nothing Then
                Throw New InvalidOperationException("工具管理器未创建，无法启动 MCP 服务")
            End If

            If Not _toolManager.IsInitialized Then
                Throw New InvalidOperationException("工具管理器未初始化，请先选择 Visual Studio 实例")
            End If

            ' 创建并启动 MCP 服务，传入工具管理器
            _mcpService = New McpService(_selectedVsInstance.DTE2, port, Me, New DispatcherService(Dispatcher), _toolManager, New ClipboardService, New InteractionService)
            _mcpService.Start()

            ' 更新 UI 状态
            _isServiceRunning = True
            UpdateServiceUI(True)

            ' 保存配置
            PersistenceModule.SaveServiceConfig(port)

            ' 记录日志
            LogServiceAction(My.Resources.LogServiceStarted, My.Resources.LogSuccess, String.Format(My.Resources.LogServiceStartedWithDetails, port, _selectedVsInstance.Caption, _toolManager.GetToolCount()))

            ' 权限已在选择实例时同步，无需再次同步
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
            LogServiceAction(My.Resources.LogServiceStopped, My.Resources.LogSuccess, My.Resources.LogUserStopped)
        Catch ex As Exception
            CleanupService()
            Throw
        End Try
    End Sub

    Private Sub OnVisualStudioExited(sender As Object, e As EventArgs)
        UtilityModule.SafeBeginInvoke(Dispatcher, Sub()
                                                      LogServiceAction(My.Resources.LogVsExited, My.Resources.LogFailed, My.Resources.LogVsInstanceServiceStop)
                                                      CleanupService()
                                                      UtilityModule.ShowWarning(Me, My.Resources.MsgVsInstanceExited, My.Resources.TitleInstanceExited)
                                                  End Sub)
    End Sub

    Private Sub OnVisualStudioShutdown(sender As Object, e As EventArgs)
        UtilityModule.SafeBeginInvoke(Dispatcher, Sub()
                                                      LogServiceAction(My.Resources.LogVsShutdown, My.Resources.LogFailed, My.Resources.LogVsInstanceServiceStopClosing)
                                                      CleanupService()
                                                      UtilityModule.ShowWarning(Me, My.Resources.MsgVsInstanceClosing, My.Resources.TitleInstanceClosing)
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

            ' 工具管理器不再清理，保持其生命周期与应用程序一致

            _isServiceRunning = False
            UpdateServiceUI(False)

        Catch ex As Exception
            ' 忽略清理时的错误
        End Try
    End Sub

    Private Sub UpdateServiceUI(isRunning As Boolean)
        If isRunning Then
            TxtServiceStatus.Text = String.Format(My.Resources.StatusServiceRunning, TxtPort.Text)
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

            ' 服务停止时，清理选中实例，但保持工具管理器
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
            Dim serverName = $"@nukepayload2/devenv.wrapper"
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
            Dim claudeConfig As String = $"claude mcp add --transport http {serverName} ""http://localhost:{port}/mcp/"""

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