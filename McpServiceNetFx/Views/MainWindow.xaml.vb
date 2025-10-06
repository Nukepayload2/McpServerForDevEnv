Partial Public Class MainWindow
    Private _isLoaded As Boolean = False
    Private _toolManager As VisualStudioToolManager

    ''' <summary>
    ''' 获取当前的工具管理器实例
    ''' </summary>
    Public ReadOnly Property ToolManager As VisualStudioToolManager
        Get
            Return _toolManager
        End Get
    End Property

    Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If _isLoaded Then Return
        _isLoaded = True
        InitializeApplication()
    End Sub

    Private Sub InitializeApplication()
        ' 初始化各个功能模块
        InitializeVsInstances()
        InitializeToolManager()
        InitializePermissions()
        InitializeMcpService()
        InitializeLogging()
    End Sub

    Private Sub InitializeVsInstances()
        Dispatcher.BeginInvoke(Sub() RefreshVsInstances())
    End Sub

    Private Sub InitializeToolManager()
        Try
            ' 在应用启动时创建工具管理器框架
            _toolManager = New VisualStudioToolManager(Me, Me)
            LogOperation(My.Resources.LogToolManager, My.Resources.LogCompleted, My.Resources.LogFrameworkCreated)
        Catch ex As Exception
            LogOperation(My.Resources.LogToolManager, My.Resources.LogFailed, ex.Message)
            UtilityModule.ShowError(Me, String.Format(My.Resources.MsgCreateToolManagerContextFailed, ex.Message))
        End Try
    End Sub

    Private Sub InitializePermissions()
        LoadPermissions()
    End Sub

    Private Sub InitializeMcpService()
        LoadServiceConfig()
    End Sub

    Private Sub MainWindow_Activated() Handles Me.Activated
        WpfVBHost.Instance.CurrentWindow = Me
    End Sub

    Private Sub InitializeLogging()
        ' 不再加载历史日志，仅初始化内存日志集合
        DgLogs.ItemsSource = _logs
    End Sub
End Class
