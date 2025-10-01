Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Linq
Imports System.Runtime.CompilerServices
Imports System.Text.Json
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input

Partial Public Class SymbolSearchControl
    Inherits UserControl
    Implements INotifyPropertyChanged

    Private _searchInProgress As Boolean = False
    Private _lastSearchTerm As String = String.Empty

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Protected Overridable Sub OnPropertyChanged(<CallerMemberName> Optional propertyName As String = Nothing)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
    End Sub

    Public Property SearchInProgress As Boolean
        Get
            Return _searchInProgress
        End Get
        Set(value As Boolean)
            If _searchInProgress <> value Then
                _searchInProgress = value
                OnPropertyChanged()
                OnPropertyChanged(NameOf(CanSearch))
            End If
        End Set
    End Property

    Public ReadOnly Property CanSearch As Boolean
        Get
            Return Not SearchInProgress AndAlso Not String.IsNullOrWhiteSpace(SearchTextBox.Text)
        End Get
    End Property

    Private Async Sub SearchTextBox_TextChanged(sender As Object, e As TextChangedEventArgs)
        ' 如果搜索正在进行中，不触发新的搜索
        If SearchInProgress Then Return

        ' 防抖处理：等待用户停止输入
        Dim searchTerm As String = SearchTextBox.Text.Trim()

        If searchTerm = _lastSearchTerm Then Return

        ' 简单的防抖：等待500ms后再搜索
        Await Task.Delay(500)

        ' 检查文本是否还在相同（防止在等待期间用户继续输入）
        If SearchTextBox.Text.Trim() = searchTerm Then
            _lastSearchTerm = searchTerm
            If String.IsNullOrWhiteSpace(searchTerm) Then
                ResultTextBox.Text = "{}"
                StatusTextBlock.Text = "准备搜索符号..."
            Else
                Await PerformSearchAsync(searchTerm)
            End If
        End If
    End Sub

    Private Async Sub SearchButton_Click(sender As Object, e As RoutedEventArgs)
        Dim searchTerm As String = SearchTextBox.Text.Trim()
        If String.IsNullOrWhiteSpace(searchTerm) Then
            MessageBox.Show("请输入搜索关键词", "提示", MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If

        Await PerformSearchAsync(searchTerm)
    End Sub

    Private Async Function PerformSearchAsync(searchTerm As String) As Task
        If SearchInProgress Then Return

        SearchInProgress = True
        SearchButton.IsEnabled = False
        SearchTextBox.IsEnabled = False
        StatusTextBlock.Text = $"正在搜索符号: {searchTerm}..."

        Try
            ' 模拟LSP符号搜索（这里需要集成实际的LSP服务）
            Dim results = Await SearchSymbolsAsync(searchTerm)

            ' 将结果转换为JSON格式
            Dim jsonOptions As New JsonSerializerOptions With {
                .WriteIndented = True,
                .Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }

            Dim jsonResult = JsonSerializer.Serialize(results, jsonOptions)
            ResultTextBox.Text = jsonResult

            Dim resultCount As Integer = If(results?.Count, 0)
            StatusTextBlock.Text = $"找到 {resultCount} 个符号结果"
        Catch ex As Exception
            ResultTextBox.Text = $"{{""error"": ""搜索过程中发生错误: {ex.Message}""}}"
            StatusTextBlock.Text = "搜索失败"
            MessageBox.Show($"搜索符号时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error)
        Finally
            SearchInProgress = False
            SearchButton.IsEnabled = True
            SearchTextBox.IsEnabled = True
        End Try
    End Function

    Private _symbolSearchService As ISymbolSearchService

    Public Sub New()
        InitializeComponent()
        DataContext = Me

        ' 尝试初始化符号搜索服务
        Try
            ' 在实际的VS环境中，这里应该从服务容器获取服务
            ' _symbolSearchService = serviceProvider.GetService(Of ISymbolSearchService)()

            ' 使用LSP符号搜索服务
            Dim componentModel = Microsoft.VisualStudio.Shell.Package.GetGlobalService(GetType(Microsoft.VisualStudio.ComponentModelHost.SComponentModel))
            If componentModel IsNot Nothing Then
                _symbolSearchService = New LspSymbolSearchService(DirectCast(componentModel, Microsoft.VisualStudio.ComponentModelHost.IComponentModel))
            End If
        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine($"Failed to initialize symbol search service: {ex.Message}")
        End Try
    End Sub

    Private Async Function SearchSymbolsAsync(searchTerm As String) As Task(Of List(Of SymbolResult))
        ' 使用实际的LSP服务搜索符号
        Dim results = Await _symbolSearchService.SearchWorkspaceSymbolsAsync(searchTerm, CancellationToken.None)
        Return results
    End Function

    Private Async Function GetMockResultsAsync(searchTerm As String) As Task(Of List(Of SymbolResult))
        ' 模拟网络延迟
        Await Task.Delay(500)

        Dim results As New List(Of SymbolResult) From {
            New SymbolResult With {
                .Name = $"SampleClass_{searchTerm}",
                .Kind = "Class",
                .Location = New SymbolLocation With {
                    .Uri = "file:///c:/project/sample.cs",
                    .Range = New SymbolRange With {
                        .Start = New SymbolPosition With {.Line = 1, .Character = 0},
                        .End = New SymbolPosition With {.Line = 10, .Character = 0}
                    }
                },
                .ContainerName = "SampleNamespace"
            },
            New SymbolResult With {
                .Name = $"SampleMethod_{searchTerm}",
                .Kind = "Method",
                .Location = New SymbolLocation With {
                    .Uri = "file:///c:/project/sample.cs",
                    .Range = New SymbolRange With {
                        .Start = New SymbolPosition With {.Line = 5, .Character = 4},
                        .End = New SymbolPosition With {.Line = 8, .Character = 5}
                    }
                },
                .ContainerName = "SampleClass"
            },
            New SymbolResult With {
                .Name = $"SampleProperty_{searchTerm}",
                .Kind = "Property",
                .Location = New SymbolLocation With {
                    .Uri = "file:///c:/project/sample.cs",
                    .Range = New SymbolRange With {
                        .Start = New SymbolPosition With {.Line = 3, .Character = 8},
                        .End = New SymbolPosition With {.Line = 3, .Character = 20}
                    }
                },
                .ContainerName = "SampleClass"
            }
        }

        Return results
    End Function
End Class

' 符号搜索结果的数据模型
Public Class SymbolResult
        Public Property Name As String
        Public Property Kind As String
        Public Property Location As SymbolLocation
        Public Property ContainerName As String
    End Class

    Public Class SymbolLocation
        Public Property Uri As String
        Public Property Range As SymbolRange
    End Class

    Public Class SymbolRange
        Public Property [Start] As SymbolPosition
        Public Property [End] As SymbolPosition
    End Class

    Public Class SymbolPosition
        Public Property Line As Integer
        Public Property Character As Integer
    End Class
