' 被控端启动配置。pipe 名由 WpfDebugProtocol.GetPipeNameForPid(自身pid) 得出，不由 config 指定。

''' <summary>
''' <see cref="WpfDebugHost.Start"/> 的启动配置。
''' </summary>
Public Class WpfDebugHostConfig

    ''' <summary>
    ''' 是否启用 VB.NET 脚本执行（evaluate）。脚本引擎体积大，可关掉以省发布体积与启动成本。
    ''' 默认 True。实际脚本能力由后续阶段接入，本骨架阶段该开关仅作记录。
    ''' </summary>
    Public Property EnableScripting As Boolean = True

    ''' <summary>
    ''' pipe 连接接受的最多并发请求数（串行处理时为 1 即可）。
    ''' </summary>
    Public Property MaxConcurrentConnections As Integer = 1
End Class
