Partial Public Class MainWindow
    Private _isLoaded As Boolean = False

    Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        If _isLoaded Then Return
        _isLoaded = True
        InitializeApplication()
    End Sub

    Private Sub InitializeApplication()
        ' 初始化各个功能模块
        InitializeVsInstances()
        InitializePermissions()
        InitializeMcpService()
        InitializeLogging()
    End Sub

    Private Sub InitializeVsInstances()
        RefreshVsInstances()
    End Sub

    Private Sub InitializePermissions()
        LoadPermissions()
    End Sub

    Private Sub InitializeMcpService()
        LoadServiceConfig()
    End Sub

    Private Sub InitializeLogging()
        ' 不再加载历史日志，仅初始化内存日志集合
        DgLogs.ItemsSource = _logs
    End Sub
End Class
