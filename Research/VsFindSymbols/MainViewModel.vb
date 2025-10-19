Imports System.ComponentModel
Imports System.Runtime.CompilerServices
Imports System.Collections.ObjectModel
Imports System.Threading.Tasks

''' <summary>
''' 主视图模型
''' </summary>
Public Class MainViewModel
    Implements INotifyPropertyChanged

    Private _symbolName As String
    Private _selectedSearchScope As SearchScope = SearchScope.Solution
    Private _selectedSearchType As SearchType = SearchType.Definition
    Private _searchResults As New ObservableCollection(Of SymbolLocation)
    Private _selectedResult As SymbolLocation
    Private _isSearching As Boolean
    Private _statusText As String = "就绪"
    Private _searchEngine As UniversalSymbolSearchEngine
    Private _isInitialized As Boolean = False

    Public Property SymbolName As String
        Get
            Return _symbolName
        End Get
        Set(value As String)
            SetProperty(_symbolName, value)
            ' 清除搜索按钮命令重新评估
            SearchCommand?.RaiseCanExecuteChanged()
        End Set
    End Property

    Public Property SelectedSearchScope As SearchScope
        Get
            Return _selectedSearchScope
        End Get
        Set(value As SearchScope)
            SetProperty(_selectedSearchScope, value)
        End Set
    End Property

    Public Property SelectedSearchType As SearchType
        Get
            Return _selectedSearchType
        End Get
        Set(value As SearchType)
            SetProperty(_selectedSearchType, value)
        End Set
    End Property

    Public Property SearchResults As ObservableCollection(Of SymbolLocation)
        Get
            Return _searchResults
        End Get
        Set(value As ObservableCollection(Of SymbolLocation))
            SetProperty(_searchResults, value)
        End Set
    End Property

    Public Property SelectedResult As SymbolLocation
        Get
            Return _selectedResult
        End Get
        Set(value As SymbolLocation)
            SetProperty(_selectedResult, value)
        End Set
    End Property

    Public Property IsSearching As Boolean
        Get
            Return _isSearching
        End Get
        Set(value As Boolean)
            SetProperty(_isSearching, value)
            SearchCommand?.RaiseCanExecuteChanged()
        End Set
    End Property

    Public Property StatusText As String
        Get
            Return _statusText
        End Get
        Set(value As String)
            SetProperty(_statusText, value)
        End Set
    End Property

    ' 命令
    Public ReadOnly Property SearchCommand As RelayCommand
    Public ReadOnly Property ClearCommand As RelayCommand
    Public ReadOnly Property OpenFileCommand As RelayCommand

    Public Sub New()
        ' 初始化命令
        SearchCommand = New RelayCommand(AddressOf ExecuteSearchAsync, Function() CanExecuteSearch())
        ClearCommand = New RelayCommand(AddressOf ExecuteClear)
        OpenFileCommand = New RelayCommand(AddressOf ExecuteOpenFile, Function() CanExecuteOpenFile())
    End Sub

    ''' <summary>
    ''' 初始化搜索引擎
    ''' </summary>
    Public Async Function InitializeAsync() As Task
        If _isInitialized Then Return

        Try
            StatusText = "正在初始化搜索引擎..."
            _searchEngine = New UniversalSymbolSearchEngine()
            Await _searchEngine.InitializeAsync()
            StatusText = "搜索引擎已就绪 - 已连接到Visual Studio"
            _isInitialized = True
        Catch ex As Exception
            StatusText = $"初始化失败: {ex.Message}"
            _isInitialized = False
        End Try
    End Function

    Private Async Function ExecuteSearchAsync() As Task
        If String.IsNullOrWhiteSpace(SymbolName) Then
            StatusText = "请输入符号名称"
            Return
        End If

        If Not _isInitialized Then
            StatusText = "搜索引擎未初始化，正在重试..."
            Await InitializeAsync()
        End If

        If _searchEngine Is Nothing OrElse Not _isInitialized Then
            StatusText = "搜索引擎初始化失败"
            Return
        End If

        Try
            IsSearching = True
            StatusText = "正在搜索..."
            SearchResults.Clear()

            Dim results As List(Of SymbolLocation)

            Select Case SelectedSearchType
                Case SearchType.Definition
                    StatusText = $"正在搜索 '{SymbolName}' 的定义..."
                    results = Await _searchEngine.FindSymbolDefinitionsAsync(SymbolName, SelectedSearchScope)
                Case SearchType.Reference
                    StatusText = $"正在搜索 '{SymbolName}' 的引用..."
                    results = Await _searchEngine.FindSymbolReferencesAsync(SymbolName, SelectedSearchScope)
                Case SearchType.All
                    StatusText = $"正在搜索 '{SymbolName}' 的定义和引用..."
                    ' 先搜索定义
                    Dim definitions = Await _searchEngine.FindSymbolDefinitionsAsync(SymbolName, SelectedSearchScope)
                    ' 再搜索引用
                    Dim references = Await _searchEngine.FindSymbolReferencesAsync(SymbolName, SelectedSearchScope)

                    ' 合并结果
                    results = New List(Of SymbolLocation)
                    results.AddRange(definitions)
                    results.AddRange(references)
                Case Else
                    results = New List(Of SymbolLocation)
            End Select

            ' 过滤重复结果
            results = FilterDuplicateResults(results)

            ' 添加到结果集合
            For Each result In results
                SearchResults.Add(result)
            Next

            If results.Count = 0 Then
                StatusText = $"未找到符号 '{SymbolName}'"
            Else
                StatusText = $"找到 {results.Count} 个结果"
            End If

        Catch ex As Exception
            StatusText = $"搜索失败: {ex.Message}"
        Finally
            IsSearching = False
        End Try
    End Function

    Private Function CanExecuteSearch() As Boolean
        Return Not String.IsNullOrWhiteSpace(SymbolName) AndAlso Not IsSearching AndAlso _isInitialized
    End Function

    Private Sub ExecuteClear()
        SymbolName = ""
        SearchResults.Clear()
        SelectedResult = Nothing
        StatusText = "就绪"
    End Sub

    Private Function CanExecuteOpenFile() As Boolean
        Return SelectedResult IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(SelectedResult.FilePath)
    End Function

    Private Sub ExecuteOpenFile()
        Try
            If SelectedResult Is Nothing Then Return

            Dim result = SelectedResult
            If String.IsNullOrWhiteSpace(result.FilePath) Then Return

            ' 打开文件
            Process.Start(New ProcessStartInfo With {
                .FileName = result.FilePath,
                .UseShellExecute = True
            })

            StatusText = $"已打开文件: {result.FilePath}"

        Catch ex As Exception
            StatusText = $"打开文件失败: {ex.Message}"
            MessageBox.Show($"无法打开文件: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    ''' <summary>
    ''' 过滤重复结果
    ''' </summary>
    Private Function FilterDuplicateResults(results As List(Of SymbolLocation)) As List(Of SymbolLocation)
        If results Is Nothing OrElse results.Count = 0 Then Return results

        Dim uniqueResults As New List(Of SymbolLocation)
        Dim seen As New HashSet(Of String)()

        For Each result In results
            Dim key As String = $"{result.FilePath}:{result.LineNumber}:{result.ColumnNumber}:{result.SymbolName}"

            If Not seen.Contains(key) Then
                seen.Add(key)
                uniqueResults.Add(result)
            End If
        Next

        Return uniqueResults
    End Function

    ''' <summary>
    ''' 获取搜索范围的显示名称
    ''' </summary>
    Public Function GetSearchScopeDisplayName(scope As SearchScope) As String
        Select Case scope
            Case SearchScope.Solution
                Return "解决方案"
            Case SearchScope.Project
                Return "当前项目"
            Case SearchScope.Namespace
                Return "命名空间"
            Case VsFindSymbols.SearchScope.Class
                Return "类"
            Case Else
                Return scope.ToString()
        End Select
    End Function

    ''' <summary>
    ''' 获取搜索类型的显示名称
    ''' </summary>
    Public Function GetSearchTypeDisplayName(searchType As SearchType) As String
        Select Case searchType
            Case SearchType.Definition
                Return "定义"
            Case SearchType.Reference
                Return "引用"
            Case SearchType.All
                Return "全部"
            Case Else
                Return searchType.ToString()
        End Select
    End Function

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Protected Overridable Sub OnPropertyChanged(<CallerMemberName> Optional propertyName As String = Nothing)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
    End Sub

    Protected Function SetProperty(Of T)(ByRef field As T, value As T, <CallerMemberName> Optional propertyName As String = Nothing) As Boolean
        If Equals(field, value) Then Return False
        field = value
        OnPropertyChanged(propertyName)
        Return True
    End Function

    ''' <summary>
    ''' 释放资源
    ''' </summary>
    Public Sub Dispose()
        Try
            _searchEngine?.Dispose()
        Catch ex As Exception
            Debug.WriteLine($"释放MainViewModel资源时出错: {ex.Message}")
        End Try
    End Sub

    Protected Overrides Sub Finalize()
        Try
            Dispose()
        Finally
            MyBase.Finalize()
        End Try
    End Sub
End Class