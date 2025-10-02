Imports System.Collections.ObjectModel

Partial Public Class MainWindow
    Private _logs As New ObservableCollection(Of PersistenceModule.LogEntry)()

    Private Sub LoadLogs()
        Try
            Dim loadedLogs = PersistenceModule.LoadLogs()
            _logs.Clear()
            For Each log In loadedLogs.OrderByDescending(Function(l) l.Timestamp)
                _logs.Add(log)
            Next
            DgLogs.ItemsSource = _logs
        Catch ex As Exception
            UtilityModule.ShowError(Me, $"加载日志失败: {ex.Message}")
        End Try
    End Sub

    Private Sub BtnClearLogs_Click() Handles BtnClearLogs.Click
        If Not UtilityModule.ShowConfirm(Me, "确定要清空所有日志吗？此操作不可撤销。", "确认清空") Then
            Return
        End If

        Try
            _logs.Clear()
            PersistenceModule.SaveLogs(_logs)

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

        If saveDialog.ShowDialog() Then
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

        ' 仅添加到内存中的日志列表，不再立即保存到持久化存储
        _logs.Add(entry)
    End Sub

    Private Sub MainWindow_Closing() Handles Me.Closing
        Try
            ' 确保日志被保存
            If _logs.Count > 0 Then
                PersistenceModule.SaveLogs(_logs.ToList())
            End If
        Catch ex As Exception
            ' 忽略关闭时保存日志的错误
        End Try
    End Sub
End Class