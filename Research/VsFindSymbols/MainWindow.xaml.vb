Imports System.Windows
Imports System.Windows.Controls
Imports System.ComponentModel

Class MainWindow
    Private _viewModel As MainViewModel

    Private Async Sub MainWindow_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        Try
            ' 初始化数据绑定
            _viewModel = New MainViewModel()
            Me.DataContext = _viewModel

            ' 初始化UI
            InitializeUI()

            ' 异步初始化搜索引擎
            Await _viewModel.InitializeAsync()

        Catch ex As Exception
            MessageBox.Show($"初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub ViewModel_PropertyChanged(sender As Object, e As PropertyChangedEventArgs)
        Try
            If e.PropertyName = "IsSearching" Then
                SearchProgressBar.Visibility = If(_viewModel.IsSearching, Visibility.Visible, Visibility.Collapsed)
            End If
        Catch ex As Exception
            Debug.WriteLine($"处理属性变更时出错: {ex.Message}")
        End Try
    End Sub

    Private Sub InitializeUI()
        Try
            ' 设置搜索范围选项
            SearchScopeComboBox.SelectedIndex = 0 ' 解决方案

            ' 设置搜索类型选项
            SearchTypeComboBox.SelectedIndex = 0 ' 定义

            ' 设置事件处理
            AddHandler SearchScopeComboBox.SelectionChanged, AddressOf SearchScopeComboBox_SelectionChanged
            AddHandler SearchTypeComboBox.SelectionChanged, AddressOf SearchTypeComboBox_SelectionChanged

            ' 绑定双击事件
            AddHandler ResultsDataGrid.MouseDoubleClick, AddressOf ResultsDataGrid_MouseDoubleClick

            ' 绑定进度条可见性
            AddHandler _viewModel.PropertyChanged, AddressOf ViewModel_PropertyChanged

            ' 设置焦点到搜索文本框
            SymbolNameTextBox.Focus()

        Catch ex As Exception
            MessageBox.Show($"初始化UI失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub SearchScopeComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Try
            If SearchScopeComboBox.SelectedItem IsNot Nothing AndAlso _viewModel IsNot Nothing Then
                Dim selectedItem As ComboBoxItem = TryCast(SearchScopeComboBox.SelectedItem, ComboBoxItem)
                If selectedItem IsNot Nothing Then
                    Dim tag As String = CStr(selectedItem.Tag)

                    Select Case tag
                        Case "Solution"
                            _viewModel.SelectedSearchScope = SearchScope.Solution
                        Case "Project"
                            _viewModel.SelectedSearchScope = SearchScope.Project
                        Case "Namespace"
                            _viewModel.SelectedSearchScope = SearchScope.Namespace
                        Case "Class"
                            _viewModel.SelectedSearchScope = SearchScope.Class
                    End Select
                End If
            End If
        Catch ex As Exception
            MessageBox.Show($"设置搜索范围失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub SearchTypeComboBox_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        Try
            If SearchTypeComboBox.SelectedItem IsNot Nothing AndAlso _viewModel IsNot Nothing Then
                Dim selectedItem As ComboBoxItem = TryCast(SearchTypeComboBox.SelectedItem, ComboBoxItem)
                If selectedItem IsNot Nothing Then
                    Dim tag As String = CStr(selectedItem.Tag)

                    Select Case tag
                        Case "Definition"
                            _viewModel.SelectedSearchType = SearchType.Definition
                        Case "Reference"
                            _viewModel.SelectedSearchType = SearchType.Reference
                        Case "All"
                            _viewModel.SelectedSearchType = SearchType.All
                    End Select
                End If
            End If
        Catch ex As Exception
            MessageBox.Show($"设置搜索类型失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub ResultsDataGrid_MouseDoubleClick(sender As Object, e As MouseButtonEventArgs)
        Try
            If _viewModel.SelectedResult IsNot Nothing Then
                ' 调用打开文件命令
                _viewModel.OpenFileCommand.Execute(Nothing)
            End If
        Catch ex As Exception
            MessageBox.Show($"双击处理失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub SymbolNameTextBox_KeyDown(sender As Object, e As KeyEventArgs) Handles SymbolNameTextBox.KeyDown
        Try
            ' 按Enter键触发搜索
            If e.Key = Key.Enter AndAlso _viewModel IsNot Nothing Then
                e.Handled = True
                If _viewModel.SearchCommand.CanExecute(Nothing) Then
                    _viewModel.SearchCommand.Execute(Nothing)
                End If
            End If
        Catch ex As Exception
            MessageBox.Show($"按键处理失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Private Sub Window_Closing(sender As Object, e As ComponentModel.CancelEventArgs) Handles Me.Closing
        Try
            ' 清理资源
            _viewModel?.Dispose()
        Catch ex As Exception
            Debug.WriteLine($"关闭窗口时清理资源失败: {ex.Message}")
        End Try
    End Sub

    Private Async Sub Window_Activated(sender As Object, e As EventArgs) Handles Me.Activated
        Try
            ' 每次窗口激活时检查Visual Studio连接状态
            If _viewModel IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(_viewModel.StatusText) Then
                If _viewModel.StatusText.Contains("初始化失败") OrElse _viewModel.StatusText.Contains("连接") Then
                    Await _viewModel.InitializeAsync()
                End If
            End If
        Catch ex As Exception
            Debug.WriteLine($"窗口激活时检查状态失败: {ex.Message}")
        End Try
    End Sub
End Class
