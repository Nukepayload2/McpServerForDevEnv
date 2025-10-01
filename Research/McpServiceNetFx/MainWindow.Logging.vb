Imports System.ComponentModel

Partial Public Class MainWindow
    Private _logs As New List(Of PersistenceModule.LogEntry)()

    Private Sub LoadLogs()
        Try
            _logs = PersistenceModule.LoadLogs()
            DgLogs.ItemsSource = _logs.OrderByDescending(Function(l) l.Timestamp).ToList()
        Catch ex As Exception
            UtilityModule.ShowError(Me, $"加载日志失败: {ex.Message}")
        End Try
    End Sub

    Private Sub RefreshLogs()
        LoadLogs()
    End Sub

    Private Sub AddLogEntry(entry As PersistenceModule.LogEntry)
        _logs.Insert(0, entry) ' 添加到开头

        ' 保持日志数量在合理范围内（最多1000条）
        If _logs.Count > 1000 Then
            _logs = _logs.Take(1000).ToList()
        End If

        ' 更新显示
        UtilityModule.SafeBeginInvoke(Dispatcher, Sub()
                                                      DgLogs.ItemsSource = _logs.ToList()
                                                  End Sub)
    End Sub

    Private Sub BtnClearLogs_Click() Handles BtnClearLogs.Click
        If Not UtilityModule.ShowConfirm(Me, "确定要清空所有日志吗？此操作不可撤销。", "确认清空") Then
            Return
        End If

        Try
            _logs.Clear()
            PersistenceModule.SaveLogs(_logs)
            DgLogs.ItemsSource = _logs

            UtilityModule.ShowInfo(Me, "日志已清空", "操作成功")
        Catch ex As Exception
            UtilityModule.ShowError(Me, $"清空日志失败: {ex.Message}")
        End Try
    End Sub

    Private Sub BtnExportLogs_Click() Handles BtnExportLogs.Click
        If _logs.Count = 0 Then
            UtilityModule.ShowWarning(Me, "没有日志可以导出", "提示")
            Return
        End If

        Dim saveDialog As New Microsoft.Win32.SaveFileDialog With {
            .Filter = "XML 文件 (*.xml)|*.xml|所有文件 (*.*)|*.*",
            .FileName = $"mcp_logs_{DateTime.Now:yyyyMMdd_HHmmss}.xml",
            .Title = "导出日志"
        }

        If saveDialog.ShowDialog() = True Then
            Try
                PersistenceModule.ExportLogs(saveDialog.FileName, _logs)
                UtilityModule.ShowInfo(Me, $"日志已导出到: {saveDialog.FileName}", "导出成功")
            Catch ex As Exception
                UtilityModule.ShowError(Me, $"导出日志失败: {ex.Message}")
            End Try
        End If
    End Sub

    Public Sub LogOperation(operation As String, result As String, details As String)
        Dim entry As New PersistenceModule.LogEntry With {
            .Timestamp = DateTime.Now,
            .Operation = operation,
            .Result = result,
            .Details = details
        }

        ' 异步保存到持久化存储
        Task.Run(Sub() PersistenceModule.AppendLog(entry))

        ' 添加到内存中的日志列表
        AddLogEntry(entry)
    End Sub

    Public Sub LogUserAction(action As String, details As String)
        LogOperation($"用户操作 - {action}", "成功", details)
    End Sub

    Public Sub LogSystemEvent(eventType As String, details As String, Optional result As String = "信息")
        LogOperation($"系统事件 - {eventType}", result, details)
    End Sub

  
    Private Sub MainWindow_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        Try
            ' 确保日志被保存
            If _logs.Count > 0 Then
                PersistenceModule.SaveLogs(_logs)
            End If
        Catch ex As Exception
            ' 忽略关闭时保存日志的错误
        End Try
    End Sub
End Class