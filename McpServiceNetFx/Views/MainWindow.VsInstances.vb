Imports System.Windows.Threading

Partial Public Class MainWindow
    Private _vsInstances As New List(Of VisualStudioInstance)()
    Private _selectedVsInstance As VisualStudioInstance
    Private _searchTimer As DispatcherTimer

    Private Sub RefreshVsInstances()
        Try
            _vsInstances = VisualStudioEnumerator.GetRunningInstances().ToList()
            DgVsInstances.ItemsSource = _vsInstances

            ' 更新选中实例信息
            If _selectedVsInstance IsNot Nothing Then
                Dim stillExists = _vsInstances.Any(Function(inst) inst.ProcessId = _selectedVsInstance.ProcessId)
                If Not stillExists Then
                    _selectedVsInstance = Nothing
                    UpdateSelectedInstanceDisplay()
                End If
            End If

        Catch ex As Exception
            UtilityModule.ShowError(Me, String.Format(My.Resources.MsgRefreshVsInstancesFailed, ex.Message))
        End Try
    End Sub

    Private Sub BtnRefresh_Click() Handles BtnRefresh.Click
        RefreshVsInstances()
    End Sub

    Private Async Sub TxtSearch_TextChanged(sender As Object, e As TextChangedEventArgs) Handles TxtSearch.TextChanged
        Await UtilityModule.DebounceAction(Dispatcher, 500, "VsInstanceSearch", Sub() FilterVsInstances())
    End Sub

    Private Sub FilterVsInstances()
        Dim searchText = TxtSearch.Text.Trim()

        If String.IsNullOrEmpty(searchText) Then
            DgVsInstances.ItemsSource = _vsInstances
        Else
            Dim filtered = UtilityModule.FilterCollection(_vsInstances, searchText, Function(inst)
                                                                                 Return $"{inst.Caption} {inst.SolutionPath} {inst.Version}"
                                                                             End Function)
            DgVsInstances.ItemsSource = filtered
        End If
    End Sub

    Private Sub DgVsInstances_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles DgVsInstances.SelectionChanged
        _selectedVsInstance = TryCast(DgVsInstances.SelectedItem, VisualStudioInstance)

        If _selectedVsInstance IsNot Nothing Then
            ' 为工具管理器创建数据上下文
            CreateToolManagerDataContext()
        Else
            ' 工具管理器保持存在，只是没有数据上下文
            LogOperation(My.Resources.LogToolManager, My.Resources.LogSkipped, My.Resources.LogInstanceCanceled)
        End If

        UpdateSelectedInstanceDisplay()
    End Sub

    ''' <summary>
    ''' 为工具管理器创建数据上下文
    ''' </summary>
    Private Sub CreateToolManagerDataContext()
        Try
            If _selectedVsInstance Is Nothing Then
                Throw New ArgumentException("必须先选择 Visual Studio 实例")
            End If

            If _toolManager Is Nothing Then
                Throw New InvalidOperationException("工具管理器框架未创建")
            End If

            ' 创建数据上下文并注册工具
            _toolManager.CreateVsTools(_selectedVsInstance.DTE2, New DispatcherService(Dispatcher))

            LogOperation(My.Resources.LogToolManager, My.Resources.LogCompleted, String.Format(My.Resources.LogInstanceAndToolCount, _selectedVsInstance.Caption, _toolManager.GetToolCount()))

            ' 重新加载权限以包含新工具的权限项
            LoadPermissions()

        Catch ex As Exception
            LogOperation(My.Resources.LogToolManager, My.Resources.LogFailed, ex.Message)
            UtilityModule.ShowError(Me, String.Format(My.Resources.MsgCreateToolManagerContextFailed, ex.Message))
        End Try
    End Sub

    Private Sub UpdateSelectedInstanceDisplay()
        If _selectedVsInstance IsNot Nothing Then
            TxtSelectedInstance.Text = $"{_selectedVsInstance.Caption} - {UtilityModule.GetFileDisplayName(_selectedVsInstance.SolutionPath)}"
        Else
            TxtSelectedInstance.Text = My.Resources.MsgNoInstanceSelected
        End If
    End Sub
End Class