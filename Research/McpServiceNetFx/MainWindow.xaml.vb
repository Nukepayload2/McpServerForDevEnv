Partial Public Class MainWindow
    Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
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
        LoadLogs()
    End Sub
End Class
