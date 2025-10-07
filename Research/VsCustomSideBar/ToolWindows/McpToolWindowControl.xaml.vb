Imports System
Imports System.Linq
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Data

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
        End Sub

        Private Sub InitializeDataBindings()
            ' 绑定工具数据
            ToolsDataGrid.ItemsSource = _state.Tools

            ' 绑定服务数据
            ServicesDataGrid.ItemsSource = _state.Services

            ' 更新状态文本
            UpdateStatusBar()
        End Sub

        Private Sub SetupEventHandlers()
            ' 工具管理按钮事件
            AddHandler AuthorizeAllButton.Click, AddressOf AuthorizeAll_Click
            AddHandler RevokeAllButton.Click, AddressOf RevokeAll_Click
            AddHandler RefreshToolsButton.Click, AddressOf RefreshTools_Click

            ' 服务管理按钮事件
            AddHandler StartAllButton.Click, AddressOf StartAll_Click
            AddHandler StopAllButton.Click, AddressOf StopAll_Click
            AddHandler RefreshServicesButton.Click, AddressOf RefreshServices_Click

            ' 数据变更事件
            AddHandler ToolsDataGrid.CurrentCellChanged, AddressOf ToolsDataGrid_CurrentCellChanged
            AddHandler ServicesDataGrid.CurrentCellChanged, AddressOf ServicesDataGrid_CurrentCellChanged
        End Sub

        Private Sub AuthorizeAll_Click(sender As Object, e As RoutedEventArgs)
            For Each tool In _state.Tools
                tool.IsAuthorized = True
                tool.IsEnabled = True
            Next

            UpdateStatusBar()
            ShowStatusMessage("所有工具已授权")
        End Sub

        Private Sub RevokeAll_Click(sender As Object, e As RoutedEventArgs)
            For Each tool In _state.Tools
                tool.IsAuthorized = False
                tool.IsEnabled = False
            Next

            UpdateStatusBar()
            ShowStatusMessage("所有工具授权已撤销")
        End Sub

        Private Sub RefreshTools_Click(sender As Object, e As RoutedEventArgs)
            ' 模拟刷新数据
            ToolsDataGrid.Items.Refresh()
            ShowStatusMessage("工具列表已刷新")
        End Sub

        Private Sub StartAll_Click(sender As Object, e As RoutedEventArgs)
            For Each service In _state.Services
                service.IsRunning = True
                service.Status = "运行中"
                service.StartTime = DateTime.Now
            Next

            UpdateStatusBar()
            ShowStatusMessage("所有服务已启动")
        End Sub

        Private Sub StopAll_Click(sender As Object, e As RoutedEventArgs)
            For Each service In _state.Services
                service.IsRunning = False
                service.Status = "已停止"
                service.StartTime = Nothing
            Next

            UpdateStatusBar()
            ShowStatusMessage("所有服务已停止")
        End Sub

        Private Sub RefreshServices_Click(sender As Object, e As RoutedEventArgs)
            ' 模拟刷新数据
            ServicesDataGrid.Items.Refresh()
            ShowStatusMessage("服务状态已刷新")
        End Sub

        Private Sub ToolsDataGrid_CurrentCellChanged(sender As Object, e As EventArgs)
            ' 工具状态变更时的处理
            If ToolsDataGrid.SelectedItem IsNot Nothing Then
                Dim tool = CType(ToolsDataGrid.SelectedItem, McpToolState)
                If tool IsNot Nothing Then
                    ' 确保未授权的工具不能启用
                    If Not tool.IsAuthorized AndAlso tool.IsEnabled Then
                        tool.IsEnabled = False
                        ShowStatusMessage("工具必须先授权才能启用")
                    End If

                    ' 更新使用次数（模拟）
                    If tool.IsEnabled Then
                        tool.LastUsed = DateTime.Now
                    End If
                End If
            End If

            UpdateStatusBar()
        End Sub

        Private Sub ServicesDataGrid_CurrentCellChanged(sender As Object, e As EventArgs)
            ' 服务状态变更时的处理
            If ServicesDataGrid.SelectedItem IsNot Nothing Then
                Dim service = CType(ServicesDataGrid.SelectedItem, McpServiceState)
                If service IsNot Nothing Then
                    If service.IsRunning Then
                        service.Status = "运行中"
                        service.StartTime = DateTime.Now
                    Else
                        service.Status = "已停止"
                        service.StartTime = Nothing
                    End If
                End If
            End If

            UpdateStatusBar()
        End Sub

        Private Sub UpdateStatusBar()
            ' 更新工具计数
            Dim authorizedCount = _state.Tools.Where(Function(t) t.IsAuthorized).Count
            Dim enabledCount = _state.Tools.Where(Function(t) t.IsEnabled).Count
            ToolCountText.Text = $"工具: {_state.Tools.Count} (授权: {authorizedCount}, 启用: {enabledCount})"

            ' 更新服务计数
            Dim runningCount = _state.Services.Where(Function(s) s.IsRunning).Count
            ServiceCountText.Text = $"服务: {_state.Services.Count} (运行中: {runningCount})"
        End Sub

        Private Sub ShowStatusMessage(message As String)
            StatusText.Text = message
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