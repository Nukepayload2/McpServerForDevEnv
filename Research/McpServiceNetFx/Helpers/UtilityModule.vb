Imports System.Windows.Threading
Imports System.IO

Public Module UtilityModule
    Public Sub SafeInvoke(dispatcher As Dispatcher, action As Action)
        If dispatcher Is Nothing Then
            action.Invoke()
            Return
        End If

        If dispatcher.CheckAccess() Then
            action.Invoke()
        Else
            dispatcher.Invoke(action)
        End If
    End Sub

    Public Sub SafeBeginInvoke(dispatcher As Dispatcher, action As Action)
        If dispatcher Is Nothing Then
            action.Invoke()
            Return
        End If

        If dispatcher.CheckAccess() Then
            action.Invoke()
        Else
            dispatcher.BeginInvoke(action)
        End If
    End Sub

    Public Async Function SafeInvokeAsync(dispatcher As Dispatcher, func As Func(Of Task)) As Task
        If dispatcher Is Nothing Then
            Throw New ArgumentNullException(NameOf(dispatcher))
        End If

        If dispatcher.CheckAccess() Then
            Await func.Invoke()
        Else
            Dim tcs As New TaskCompletionSource(Of Boolean)
            Await dispatcher.BeginInvoke(
                Sub()
                    Try
                        func.Invoke().ContinueWith(
                        Sub(t)
                            If t.IsFaulted OrElse t.IsCanceled Then
                                tcs.SetException(t.Exception)
                            Else
                                tcs.SetResult(True)
                            End If
                        End Sub)
                    Catch ex As Exception
                        tcs.SetException(ex)
                    End Try
                End Sub)
            Await tcs.Task
        End If
    End Function

    Public Function IsValidPort(port As String) As Boolean
        If String.IsNullOrWhiteSpace(port) Then Return False

        Dim portNumber As Integer
        If Not Integer.TryParse(port, portNumber) Then Return False

        Return portNumber > 0 AndAlso portNumber <= 65535
    End Function

    Public Function GetValidPort(port As String, defaultPort As Integer) As Integer
        If IsValidPort(port) Then
            Return Integer.Parse(port)
        Else
            Return defaultPort
        End If
    End Function

    Public Sub ShowError(window As Window, message As String, Optional title As String = "错误")
        CustomMessageBox.Show(window, message, title, CustomMessageBox.MessageBoxType.Error)
    End Sub

    Public Sub ShowWarning(window As Window, message As String, Optional title As String = "警告")
        CustomMessageBox.Show(window, message, title, CustomMessageBox.MessageBoxType.Warning)
    End Sub

    Public Sub ShowInfo(window As Window, message As String, Optional title As String = "信息")
        CustomMessageBox.Show(window, message, title, CustomMessageBox.MessageBoxType.Information)
    End Sub

    Public Function ShowConfirm(window As Window, message As String, Optional title As String = "确认") As Boolean
        Return CustomMessageBox.Show(window, message, title, CustomMessageBox.MessageBoxType.Question, True) = CustomMessageBox.MessageBoxResult.OK
    End Function

    Public Function ShowConfirmModal(window As Window, message As String, Optional title As String = "确认") As Boolean
        Return CustomMessageBox.Show(window, message, title, CustomMessageBox.MessageBoxType.Question, True) = CustomMessageBox.MessageBoxResult.OK
    End Function

    Public Function GetFileDisplayName(filePath As String) As String
        If String.IsNullOrEmpty(filePath) Then
            Return "无文件"
        End If

        Try
            Dim fileName As String = Path.GetFileName(filePath)
            If String.IsNullOrEmpty(fileName) Then
                Return filePath
            End If

            Dim directory As String = Path.GetDirectoryName(filePath)
            If String.IsNullOrEmpty(directory) Then
                Return fileName
            End If

            ' 如果路径太长，缩短目录部分
            If directory.Length > 50 Then
                directory = "..." + directory.Substring(directory.Length - 47)
            End If

            Return Path.Combine(directory, fileName)
        Catch ex As Exception
            Return filePath
        End Try
    End Function

    Public Function ContainsIgnoreCase(text As String, search As String) As Boolean
        If String.IsNullOrEmpty(text) OrElse String.IsNullOrEmpty(search) Then
            Return False
        End If

        Return text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    Public Function FilterCollection(Of T)(collection As IEnumerable(Of T), searchText As String, selector As Func(Of T, String)) As IEnumerable(Of T)
        If String.IsNullOrWhiteSpace(searchText) Then
            Return collection
        End If

        Return collection.Where(Function(item) ContainsIgnoreCase(selector(item), searchText))
    End Function

    Private _debounceTimers As New Dictionary(Of String, DispatcherTimer)()

    Public Function DebounceAction(dispatcher As Dispatcher, delay As Integer, key As String, action As Action) As Task
        ' 停止之前的计时器（如果存在）
        If _debounceTimers.ContainsKey(key) Then
            _debounceTimers(key).Stop()
            _debounceTimers.Remove(key)
        End If

        Dim timer As New DispatcherTimer With {
            .Interval = TimeSpan.FromMilliseconds(delay)
        }

        Dim tcs As New TaskCompletionSource(Of Boolean)()
        AddHandler timer.Tick, Sub(sender, e)
                                  timer.Stop()
                                  _debounceTimers.Remove(key)
                                  SafeBeginInvoke(dispatcher, Sub()
                                                                     action.Invoke()
                                                                     tcs.SetResult(True)
                                                                 End Sub)
                              End Sub

        _debounceTimers(key) = timer
        timer.Start()
        Return tcs.Task
    End Function

End Module