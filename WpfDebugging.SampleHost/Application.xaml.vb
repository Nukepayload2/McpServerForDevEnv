Imports System.Windows

' SampleHost 应用入口。
' OnStartup 启动 WpfDebugHost（起固定名 pipe server），OnExit 停止清理。
' pipe 名固定（WpfDebugProtocol.PipeName），主控 WpfDebugProxy 用同名连接。
' 单被控语义：第二个 SampleHost 启动时同名 pipe 已占用会抛 IOException，直接报错退出。

Partial Class Application

    ' 被控端宿主实例。OnStartup 构造，OnExit 停止。
    Private _host As McpServerForDevEnv.WpfDebugging.Target.WpfDebugHost

    Protected Overrides Sub OnStartup(e As StartupEventArgs)
        MyBase.OnStartup(e)

        ' 启动被控端：起 pipe server 监听主控连接。
        ' 同名 pipe 已被占用（已有被控端在跑）时抛 IOException——单被控语义，直接崩给用户看。
        _host = McpServerForDevEnv.WpfDebugging.Target.WpfDebugHost.Start()

        ' 显示主窗口（放测试控件供联调）。
        Dim window As New MainWindow(_host)
        window.Show()
    End Sub

    Protected Overrides Sub OnExit(e As ExitEventArgs)
        ' 停止 pipe server + 清理 uid 映射。幂等。
        Try
            _host?.Stop()
        Catch
            ' 退出阶段异常吞掉，不挡进程关闭。
        Finally
            _host?.Dispose()
        End Try

        MyBase.OnExit(e)
    End Sub

End Class
