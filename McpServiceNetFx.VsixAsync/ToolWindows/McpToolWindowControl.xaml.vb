Imports System
Imports System.Diagnostics
Imports System.Windows.Controls
Imports System.Windows.Data
Imports System.Windows.Media
Imports System.Globalization

Namespace ToolWindows
    ''' <summary>
    ''' PermissionLevel 值转换器
    ''' </summary>
    Public Class PermissionLevelConverter
        Implements IValueConverter

        Public Function Convert(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.Convert
            If TypeOf value Is PermissionLevel Then
                Dim permissionLevel = CType(value, PermissionLevel)
                Select Case permissionLevel
                    Case PermissionLevel.Allow
                        Return "Allow"
                    Case PermissionLevel.Ask
                        Return "Ask"
                    Case PermissionLevel.Deny
                        Return "Deny"
                    Case Else
                        Return "Ask"
                End Select
            End If
            Return "Ask"
        End Function

        Public Function ConvertBack(value As Object, targetType As Type, parameter As Object, culture As CultureInfo) As Object Implements IValueConverter.ConvertBack
            If TypeOf value Is String Then
                Dim stringValue = CStr(value)
                Select Case stringValue
                    Case "Allow"
                        Return PermissionLevel.Allow
                    Case "Ask"
                        Return PermissionLevel.Ask
                    Case "Deny"
                        Return PermissionLevel.Deny
                    Case Else
                        Return PermissionLevel.Ask
                End Select
            End If
            Return PermissionLevel.Ask
        End Function
    End Class

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

            ' 记录初始化日志
            _state.LogInfo("System", "MCP 服务管理器已启动")
        End Sub

        Private Sub InitializeDataBindings()
            ' 绑定工具数据
            ToolsDataGrid.ItemsSource = _state.Tools

            ' 绑定日志数据
            ActivityLogDataGrid.ItemsSource = _state.LogItems

            ' 初始化配置示例
            McpJsonConfigTextBox.Text = _state.GetMcpJsonConfig()
            ClaudeCliConfigTextBox.Text = _state.GetClaudeCliConfig()

            ' 初始化端口号显示
            PortNumberTextBox.Text = _state.ServerConfiguration.Port.ToString()

            ' 更新服务状态显示
            UpdateServiceStatusDisplay()

            ' 更新状态文本
            UpdateStatusBar()
        End Sub

        Private Sub AuthorizeAll_Click() Handles AuthorizeAllButton.Click
            _state.SetAllPermissions(PermissionLevel.Allow)
            UpdateStatusBar()
            ShowStatusMessage("所有工具权限已设置为允许")
        End Sub

        Private Sub AskAll_Click() Handles AskAllButton.Click
            _state.SetAllPermissions(PermissionLevel.Ask)
            UpdateStatusBar()
            ShowStatusMessage("所有工具权限已设置为询问")
        End Sub

        Private Sub SavePermissions_Click() Handles SavePermissionsButton.Click
            _state.SavePermissions()
            ShowStatusMessage("权限配置已保存")
        End Sub

        Private Sub ReloadPermissions_Click() Handles ReloadPermissionsButton.Click
            _state.ReloadPermissions()
            ToolsDataGrid.Items.Refresh()
            UpdateStatusBar()
            ShowStatusMessage("权限配置已重新加载")
        End Sub

        Private Sub RefreshTools_Click() Handles RefreshToolsButton.Click
            ' 模拟刷新数据
            ToolsDataGrid.Items.Refresh()
            _state.LogInfo("ToolOperation", "工具列表已刷新")
            ShowStatusMessage("工具列表已刷新")
        End Sub

        Private Async Sub ServiceToggleButton_Checked() Handles ServiceToggleButton.Checked
            Await _state.StartServiceAsync()
            UpdateServiceStatusDisplay()
            UpdateStatusBar()
            ShowStatusMessage("MCP 服务已启动")
        End Sub

        Private Sub ServiceToggleButton_Unchecked() Handles ServiceToggleButton.Unchecked
            _state.StopService()
            UpdateServiceStatusDisplay()
            UpdateStatusBar()
            ShowStatusMessage("MCP 服务已停止")
        End Sub

        Private Sub UpdateServiceStatusDisplay()
            If _state.Services.Count > 0 Then
                Dim service = _state.Services(0)
                ServiceStatusText.Text = $"状态: {service.Status}"

                If service.IsRunning Then
                    ServiceToggleButton.IsChecked = True
                    ServiceToggleButton.Content = "停止服务"
                    ServiceToggleButton.Background = New SolidColorBrush(Colors.Red)
                    ' 禁用端口号输入框和重置按钮
                    PortNumberTextBox.IsEnabled = False
                    ResetPortButton.IsEnabled = False
                Else
                    ServiceToggleButton.IsChecked = False
                    ServiceToggleButton.Content = "启动服务"
                    ServiceToggleButton.Background = New SolidColorBrush(Color.FromRgb(40, 167, 69))
                    ' 启用端口号输入框和重置按钮
                    PortNumberTextBox.IsEnabled = True
                    ResetPortButton.IsEnabled = True
                End If
            End If
        End Sub

        Private Sub ToolsDataGrid_CurrentCellChanged() Handles ToolsDataGrid.CurrentCellChanged
            ' 工具权限变更时的处理
            If ToolsDataGrid.SelectedItem IsNot Nothing Then
                Dim tool = CType(ToolsDataGrid.SelectedItem, PermissionItem)
                If tool IsNot Nothing Then
                    ' 记录工具权限变更
                    _state.LogToolOperation("UpdateToolPermission", "Success", $"工具 {tool.FeatureName} 权限已更新为: {tool.Permission}")
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

        Private Sub ViewActivityLogHelpButton_Click() Handles ViewActivityLogHelpButton.Click
            Try
                Dim helpUrl = "https://learn.microsoft.com/zh-cn/visualstudio/extensibility/how-to-use-the-activity-log?view=vs-2022#to-examine-the-activity-log"
                System.Diagnostics.Process.Start(New ProcessStartInfo With {
                    .FileName = helpUrl,
                    .UseShellExecute = True
                })
                _state.LogInfo("HelpAction", "打开 ActivityLog 帮助文档")
            Catch ex As Exception
                _state.LogError("HelpAction", $"无法打开帮助文档: {ex.Message}")
                CustomMessageBox.Show(Nothing, $"无法打开帮助文档: {ex.Message}", "错误", CustomMessageBox.MessageBoxType.Error)
            End Try
        End Sub

        Private Sub ClearLogButton_Click() Handles ClearLogButton.Click
            Try
                _state.ClearLogItems()
                ActivityLogDataGrid.Items.Refresh()
                ShowStatusMessage("界面日志已清空")
            Catch ex As Exception
                _state.LogError("UIAction", $"清空日志失败: {ex.Message}")
                CustomMessageBox.Show(Nothing, $"清空日志失败: {ex.Message}", "错误", CustomMessageBox.MessageBoxType.Error)
            End Try
        End Sub

        Private Sub PortNumberTextBox_LostFocus() Handles PortNumberTextBox.LostFocus
            Try
                Dim newPort As Integer
                If Integer.TryParse(PortNumberTextBox.Text, newPort) Then
                    If newPort > 0 AndAlso newPort <= 65535 Then
                        ' 检查服务是否正在运行
                        If _state.Services.Count > 0 AndAlso _state.Services(0).IsRunning Then
                            CustomMessageBox.Show(Nothing, "请先停止 MCP 服务，然后再修改端口号", "提示", CustomMessageBox.MessageBoxType.Information)
                            PortNumberTextBox.Text = _state.ServerConfiguration.Port.ToString()
                            Return
                        End If

                        ' 检查端口号是否已经是最新的，避免重复保存
                        If _state.ServerConfiguration.Port = newPort Then
                            Return
                        End If

                        ' 保存新的端口号
                        _state.ServerConfiguration.Port = newPort
                        _state.SaveServerConfiguration()

                        ' 更新配置示例
                        McpJsonConfigTextBox.Text = _state.GetMcpJsonConfig()
                        ClaudeCliConfigTextBox.Text = _state.GetClaudeCliConfig()

                        ' 更新服务状态显示
                        UpdateServiceStatusDisplay()

                        ShowStatusMessage($"端口号已自动保存为: {newPort}")
                        _state.LogInfo("Configuration", $"端口号已自动保存为: {newPort}")
                    Else
                        CustomMessageBox.Show(Nothing, "端口号必须在 1-65535 范围内", "错误", CustomMessageBox.MessageBoxType.Warning)
                        PortNumberTextBox.Text = _state.ServerConfiguration.Port.ToString()
                    End If
                Else
                    CustomMessageBox.Show(Nothing, "请输入有效的端口号", "错误", CustomMessageBox.MessageBoxType.Warning)
                    PortNumberTextBox.Text = _state.ServerConfiguration.Port.ToString()
                End If
            Catch ex As Exception
                _state.LogError("Configuration", $"自动保存端口号失败: {ex.Message}")
                CustomMessageBox.Show(Nothing, $"自动保存端口号失败: {ex.Message}", "错误", CustomMessageBox.MessageBoxType.Error)
                PortNumberTextBox.Text = _state.ServerConfiguration.Port.ToString()
            End Try
        End Sub

        Private Sub ResetPortButton_Click() Handles ResetPortButton.Click
            Try
                ' 检查服务是否正在运行
                If _state.Services.Count > 0 AndAlso _state.Services(0).IsRunning Then
                    CustomMessageBox.Show(Nothing, "请先停止 MCP 服务，然后再重置端口号", "提示", CustomMessageBox.MessageBoxType.Information)
                    Return
                End If

                ' 重置为默认端口
                _state.ServerConfiguration.Port = 38080
                _state.SaveServerConfiguration()

                ' 更新界面
                PortNumberTextBox.Text = "38080"

                ' 更新配置示例
                McpJsonConfigTextBox.Text = _state.GetMcpJsonConfig()
                ClaudeCliConfigTextBox.Text = _state.GetClaudeCliConfig()

                ' 更新服务状态显示
                UpdateServiceStatusDisplay()

                ShowStatusMessage("端口号已重置为默认值: 38080")
                _state.LogInfo("Configuration", "端口号已重置为默认值: 38080")
            Catch ex As Exception
                _state.LogError("Configuration", $"重置端口号失败: {ex.Message}")
                CustomMessageBox.Show(Nothing, $"重置端口号失败: {ex.Message}", "错误", CustomMessageBox.MessageBoxType.Error)
            End Try
        End Sub
    End Class
End Namespace