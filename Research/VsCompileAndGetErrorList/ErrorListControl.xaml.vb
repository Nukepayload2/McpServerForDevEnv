Imports System
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Runtime.CompilerServices
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Linq
Imports Newtonsoft.Json

Public Partial Class ErrorListControl
    Inherits UserControl
    Implements INotifyPropertyChanged

    Private _errors As New List(Of CompilationError)
    Private _jsonContent As String = "{ ""errors"": [], ""message"": ""准备就绪"" }"

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Public Property JsonContent As String
        Get
            Return _jsonContent
        End Get
        Set(value As String)
            _jsonContent = value
            NotifyPropertyChanged()
        End Set
    End Property

    Public Sub New()
        InitializeComponent()
        DataContext = Me
        SetupEventHandlers()
    End Sub

    Private Sub SetupEventHandlers()
        AddHandler RefreshButton.Click, AddressOf RefreshButton_Click
        AddHandler ClearButton.Click, AddressOf ClearButton_Click
        AddHandler CopyButton.Click, AddressOf CopyButton_Click
    End Sub

    Private Sub RefreshButton_Click(sender As Object, e As RoutedEventArgs)
        UpdateErrors(_errors)
    End Sub

    Private Sub ClearButton_Click(sender As Object, e As RoutedEventArgs)
        _errors.Clear()
        UpdateErrors(_errors)
    End Sub

    Private Sub CopyButton_Click(sender As Object, e As RoutedEventArgs)
        Try
            Clipboard.SetText(JsonContent)
            MessageBox.Show("JSON 内容已复制到剪贴板。", "复制成功", MessageBoxButton.OK, MessageBoxImage.Information)
        Catch ex As Exception
            MessageBox.Show($"复制失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub

    Public Sub UpdateErrors(errors As List(Of CompilationError))
        _errors = If(errors, New List(Of CompilationError))

        If _errors.Count = 0 Then
            JsonContent = "{ ""errors"": [], ""message"": ""没有发现编译错误"" }"
            ErrorCountText.Text = "错误数量: 0"
        Else
            Dim jsonErrors = _errors.Select(Function(e) New With {
                .errorCode = e.ErrorCode,
                .message = e.Message,
                .file = e.File,
                .line = e.Line,
                .column = e.Column,
                .project = e.Project,
                .severity = e.Severity
            }).ToList()

            Dim result = New With {
                .errors = jsonErrors,
                .count = _errors.Count,
                .timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            }

            JsonContent = JsonConvert.SerializeObject(result, Formatting.Indented)
            ErrorCountText.Text = $"错误数量: {_errors.Count}"
        End If
    End Sub

    Protected Overridable Sub NotifyPropertyChanged(<CallerMemberName> Optional propertyName As String = Nothing)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
    End Sub
End Class