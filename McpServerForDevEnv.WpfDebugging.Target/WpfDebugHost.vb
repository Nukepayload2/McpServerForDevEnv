Imports System

' 被控端宿主入口。宿主 WPF 程序在 Application.Startup 里调 WpfDebugHost.Start(config) 一次，
' 在 Application.Exit 里调返回实例的 Stop()（或 Dispose）。
'
' 职责：组装 WpfDispatcher / UidRegistry / WpfDebugTargetImpl / WpfDebugPipeServer，
' 启动固定 pipe 名监听，管理生命周期。pipe 名固定（WpfDebugProtocol.PipeName），不由 config 指定。

''' <summary>
''' 被控端宿主。组合各部件并管理生命周期。
''' </summary>
Public NotInheritable Class WpfDebugHost
    Implements IDisposable

    Private ReadOnly _dispatcher As WpfDispatcher
    Private ReadOnly _uidRegistry As UidRegistry
    Private ReadOnly _target As WpfDebugTargetImpl
    Private ReadOnly _server As WpfDebugPipeServer
    Private _disposed As Boolean

    Private Sub New(config As WpfDebugHostConfig)
        _dispatcher = New WpfDispatcher()
        _uidRegistry = New UidRegistry()
        _target = New WpfDebugTargetImpl(_dispatcher, _uidRegistry)
        _server = New WpfDebugPipeServer(_dispatcher, _target)
    End Sub

    ''' <summary>
    ''' 启动被控端。宿主在 Application.Startup 中调用。
    ''' 同名 pipe 已被占用（已有被控端在跑）时抛 IOException——单被控语义：直接报错，不排队、不换名。
    ''' </summary>
    Public Shared Function Start(Optional config As WpfDebugHostConfig = Nothing) As WpfDebugHost
        If config Is Nothing Then config = New WpfDebugHostConfig()
        Dim host As New WpfDebugHost(config)
        host._server.Start()
        Return host
    End Function

    ''' <summary>停止监听并清理 uid 映射。宿主在 Application.Exit 中调用。</summary>
    Public Sub [Stop]()
        If _disposed Then Return
        Try
            _server.Stop()
        Finally
            _uidRegistry.Clear()
        End Try
    End Sub

    ''' <summary>暴露 uid 注册表（供后续能力实现/脚本宿主 API 使用）。</summary>
    Public ReadOnly Property UidRegistry As UidRegistry
        Get
            Return _uidRegistry
        End Get
    End Property

    ''' <summary>暴露 UI 线程调度器。</summary>
    Public ReadOnly Property Dispatcher As WpfDispatcher
        Get
            Return _dispatcher
        End Get
    End Property

    ''' <summary>暴露被控端能力实现（供后续阶段填充/扩展）。</summary>
    Public ReadOnly Property Target As WpfDebugTargetImpl
        Get
            Return _target
        End Get
    End Property

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        [Stop]()
        _server.Dispose()
    End Sub
End Class
