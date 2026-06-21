' SampleHost 主窗口。放一批测试控件供主控联调（list_windows/take_snapshot/click/fill/evaluate/take_screenshot）。
' 接收 Application 传进来的 WpfDebugHost 实例（目前只为完整性保留，控件交互不直接用 host；
' evaluate 脚本通过被控端 ScriptHost 访问元素，不需要窗口侧暴露 host）。

Imports McpServerForDevEnv.WpfDebugging.Target

Class MainWindow

    Private ReadOnly _host As WpfDebugHost

    Public Sub New(host As WpfDebugHost)
        InitializeComponent()
        _host = host
    End Sub

    ' Button 点击：回显到状态文本（click 工具调用后有可观察的效果，便于联调验证）。
    Private Sub ClickButton_Click() Handles ClickButton.Click
        StatusTextBlock.Text = $"按钮已点击，时间 {DateTime.Now:HH:mm:ss}"
    End Sub

    ' CheckBox 切换：状态回显。
    Private Sub OptionCheckBox_Changed() Handles OptionCheckBox.Click
        StatusTextBlock.Text = $"CheckBox 状态：{If(OptionCheckBox.IsChecked = True, "勾选", "未勾")}"
    End Sub

    ' Slider 拖动：状态回显数值。
    Private Sub ValueSlider_Changed() Handles ValueSlider.ValueChanged
        ' 启动期控件还没全构造好，跳过（避免 InitializeComponent 阶段触发）。
        If StatusTextBlock Is Nothing Then Return
        StatusTextBlock.Text = $"Slider 值：{CInt(ValueSlider.Value)}"
    End Sub

End Class
