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
            UtilityModule.ShowError(Me, $"刷新 Visual Studio 实例列表失败: {ex.Message}")
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
        UpdateSelectedInstanceDisplay()
    End Sub

    Private Sub UpdateSelectedInstanceDisplay()
        If _selectedVsInstance IsNot Nothing Then
            TxtSelectedInstance.Text = $"{_selectedVsInstance.Caption} - {UtilityModule.GetFileDisplayName(_selectedVsInstance.SolutionPath)}"
            BtnAttachInstance.IsEnabled = True
        Else
            TxtSelectedInstance.Text = "未选择实例"
            BtnAttachInstance.IsEnabled = False
        End If
    End Sub
End Class