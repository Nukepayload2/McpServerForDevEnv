Imports System
Imports System.IO
Imports System.Linq
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Data
Imports System.Windows.Media
Imports Microsoft.Win32

Namespace ToolWindows
    ''' <summary>
    ''' McpToolWindowControl.xaml 的交互逻辑
    ''' </summary>
    Partial Public Class McpToolWindowControl
        Inherits UserControl

        Private _state As McpWindowState

        Public Sub New(state As McpWindowState)
            _state = state
            InitializeComponent()

            ' 初始化数据绑定
            InitializeDataBindings()

            ' 设置事件处理程序
            SetupEventHandlers()

            ' 记录初始化日志
            _state.LogInfo("System", "MCP 服务管理器已启动")
        End Sub

        Private Sub InitializeDataBindings()
            ' 绑定工具数据
            ToolsDataGrid.ItemsSource = _state.Tools

            ' 初始化配置示例
            McpJsonConfigTextBox.Text = _state.GetMcpJsonConfig()
            ClaudeCliConfigTextBox.Text = _state.GetClaudeCliConfig()

            ' 更新服务状态显示
            UpdateServiceStatusDisplay()

            ' 更新状态文本
            UpdateStatusBar()
        End Sub

        Private Sub SetupEventHandlers()
            ' 工具管理按钮事件
            AddHandler AuthorizeAllButton.Click, AddressOf AuthorizeAll_Click
            AddHandler RevokeAllButton.Click, AddressOf RevokeAll_Click
            AddHandler RefreshToolsButton.Click, AddressOf RefreshTools_Click

            ' 服务开关事件
            AddHandler ServiceToggleButton.Checked, AddressOf ServiceToggleButton_Checked
            AddHandler ServiceToggleButton.Unchecked, AddressOf ServiceToggleButton_Unchecked

            ' 数据变更事件
            AddHandler ToolsDataGrid.CurrentCellChanged, AddressOf ToolsDataGrid_CurrentCellChanged
        End Sub

        Private Sub AuthorizeAll_Click(sender As Object, e As RoutedEventArgs)
            _state.AuthorizeAllTools()
            UpdateStatusBar()
            ShowStatusMessage("所有工具已授权")
        End Sub

        Private Sub RevokeAll_Click(sender As Object, e As RoutedEventArgs)
            _state.RevokeAllToolAuthorization()
            UpdateStatusBar()
            ShowStatusMessage("所有工具授权已撤销")
        End Sub

        Private Sub RefreshTools_Click(sender As Object, e As RoutedEventArgs)
            ' 模拟刷新数据
            ToolsDataGrid.Items.Refresh()
            _state.LogInfo("ToolOperation", "工具列表已刷新")
            ShowStatusMessage("工具列表已刷新")
        End Sub

        Private Sub ServiceToggleButton_Checked(sender As Object, e As RoutedEventArgs)
            _state.StartService()
            UpdateServiceStatusDisplay()
            UpdateStatusBar()
            ShowStatusMessage("MCP 服务已启动")
        End Sub

        Private Sub ServiceToggleButton_Unchecked(sender As Object, e As RoutedEventArgs)
            _state.StopService()
            UpdateServiceStatusDisplay()
            UpdateStatusBar()
            ShowStatusMessage("MCP 服务已停止")
        End Sub

        Private Sub UpdateServiceStatusDisplay()
            If _state.Services.Count > 0 Then
                Dim service = _state.Services(0)
                ServiceStatusText.Text = $"状态: {service.Status}"
                ServicePortText.Text = $"端口: {service.Port}"

                If service.IsRunning Then
                    ServiceToggleButton.IsChecked = True
                    ServiceToggleButton.Content = "停止服务"
                    ServiceToggleButton.Background = New SolidColorBrush(Colors.Red)
                Else
                    ServiceToggleButton.IsChecked = False
                    ServiceToggleButton.Content = "启动服务"
                    ServiceToggleButton.Background = New SolidColorBrush(Color.FromRgb(40, 167, 69))
                End If
            End If
        End Sub


        Private Sub ToolsDataGrid_CurrentCellChanged(sender As Object, e As EventArgs)
            ' 工具状态变更时的处理
            If ToolsDataGrid.SelectedItem IsNot Nothing Then
                Dim tool = CType(ToolsDataGrid.SelectedItem, McpToolState)
                If tool IsNot Nothing Then
                    ' 确保未授权的工具不能启用
                    If Not tool.IsAuthorized AndAlso tool.IsEnabled Then
                        tool.IsEnabled = False
                        _state.LogWarning("ToolPermission", $"工具 {tool.ToolName} 必须先授权才能启用")
                        ShowStatusMessage("工具必须先授权才能启用")
                    Else
                        ' 记录工具状态变更
                        _state.LogToolOperation("UpdateToolStatus", "Success", $"工具 {tool.ToolName} 状态已更新 - 授权: {tool.IsAuthorized}, 启用: {tool.IsEnabled}")

                        ' 更新使用次数
                        If tool.IsEnabled Then
                            tool.LastUsed = DateTime.Now
                            tool.UsageCount += 1
                        End If
                    End If
                End If
            End If

            UpdateStatusBar()
        End Sub


        Private Sub UpdateStatusBar()
            ' 更新工具计数
            ToolCountText.Text = _state.GetToolSummary()

            ' 更新服务计数
            ServiceCountText.Text = _state.GetServiceSummary()
        End Sub

        Private Sub ShowStatusMessage(message As String)
            StatusText.Text = message
            _state.LogInfo("UIStatus", message)

            ' 3秒后恢复默认状态文本
            Dim timer = New System.Windows.Threading.DispatcherTimer With {
                .Interval = TimeSpan.FromSeconds(3)
            }
            AddHandler timer.Tick, Sub(s, args)
                                       StatusText.Text = "就绪"
                                       timer.Stop()
                                   End Sub
            timer.Start()
        End Sub
    End Class
End Namespace