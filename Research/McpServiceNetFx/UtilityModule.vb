Imports System.Windows.Threading
Imports System.ComponentModel
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
        MessageBox.Show(window, message, title, MessageBoxButton.OK, MessageBoxImage.Error)
    End Sub

    Public Sub ShowWarning(window As Window, message As String, Optional title As String = "警告")
        MessageBox.Show(window, message, title, MessageBoxButton.OK, MessageBoxImage.Warning)
    End Sub

    Public Sub ShowInfo(window As Window, message As String, Optional title As String = "信息")
        MessageBox.Show(window, message, title, MessageBoxButton.OK, MessageBoxImage.Information)
    End Sub

    Public Function ShowConfirm(window As Window, message As String, Optional title As String = "确认") As Boolean
        Return MessageBox.Show(window, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) = MessageBoxResult.Yes
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

    Public Function FormatFileSize(bytes As Long) As String
        If bytes < 0 Then Return "0 B"

        Dim units As String() = {"B", "KB", "MB", "GB", "TB"}
        Dim size As Double = bytes
        Dim unitIndex As Integer = 0

        While size >= 1024 AndAlso unitIndex < units.Length - 1
            size /= 1024
            unitIndex += 1
        End While

        Return $"{size:F1} {units(unitIndex)}"
    End Function

    Public Function FormatDuration(start As DateTime, [end] As DateTime) As String
        Dim duration = [end] - start

        If duration.TotalSeconds < 1 Then
            Return "< 1 秒"
        ElseIf duration.TotalMinutes < 1 Then
            Return $"{CInt(duration.TotalSeconds)} 秒"
        ElseIf duration.TotalHours < 1 Then
            Return $"{CInt(duration.TotalMinutes)} 分 {duration.Seconds} 秒"
        ElseIf duration.TotalDays < 1 Then
            Return $"{CInt(duration.TotalHours)} 小时 {duration.Minutes} 分"
        Else
            Return $"{CInt(duration.TotalDays)} 天 {duration.Hours} 小时"
        End If
    End Function

    Public Function GetRelativeTime(dateTime As DateTime) As String
        Dim now = DateTime.Now
        Dim span = now - dateTime

        If span.TotalSeconds < 60 Then
            Return "刚刚"
        ElseIf span.TotalMinutes < 60 Then
            Return $"{CInt(span.TotalMinutes)} 分钟前"
        ElseIf span.TotalHours < 24 Then
            Return $"{CInt(span.TotalHours)} 小时前"
        ElseIf span.TotalDays < 7 Then
            Return $"{CInt(span.TotalDays)} 天前"
        Else
            Return dateTime.ToString("yyyy-MM-dd HH:mm")
        End If
    End Function

    Public Function IsEmptyOrWhitespace(text As String) As Boolean
        Return String.IsNullOrWhiteSpace(text)
    End Function

    Public Function SafeToString(obj As Object, defaultValue As String) As String
        If obj Is Nothing Then
            Return defaultValue
        End If

        Try
            Return obj.ToString()
        Catch ex As Exception
            Return defaultValue
        End Try
    End Function

    Public Function SafeParseInt(text As String, defaultValue As Integer) As Integer
        If String.IsNullOrWhiteSpace(text) Then
            Return defaultValue
        End If

        Dim result As Integer
        Return If(Integer.TryParse(text, result), result, defaultValue)
    End Function

    Public Function SafeParseBool(text As String, defaultValue As Boolean) As Boolean
        If String.IsNullOrWhiteSpace(text) Then
            Return defaultValue
        End If

        Dim result As Boolean
        Return If(Boolean.TryParse(text, result), result, defaultValue)
    End Function

    Public Function GenerateUniqueId() As String
        Return Guid.NewGuid().ToString("N")
    End Function

    Public Function GetTimestamp() As String
        Return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
    End Function

    Public Function TruncateText(text As String, maxLength As Integer) As String
        If String.IsNullOrEmpty(text) OrElse text.Length <= maxLength Then
            Return text
        End If

        Return text.Substring(0, maxLength - 3) + "..."
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

    Public Function GetApplicationVersion() As String
        Try
            Dim assembly = System.Reflection.Assembly.GetExecutingAssembly()
            Dim version = assembly.GetName().Version
            Return $"{version.Major}.{version.Minor}.{version.Build}"
        Catch ex As Exception
            Return "未知版本"
        End Try
    End Function
End Module