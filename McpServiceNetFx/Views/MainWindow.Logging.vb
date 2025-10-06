Imports System.Collections.ObjectModel

Partial Public Class MainWindow
    Private _logs As New ObservableCollection(Of LogEntry)
    Private _appStartTime As DateTime = DateTime.Now

    Private Sub BtnClearLogs_Click() Handles BtnClearLogs.Click
        If Not UtilityModule.ShowConfirm(Me, My.Resources.MsgConfirmClearLogs, My.Resources.TitleConfirmClear) Then
            Return
        End If

        Try
            _logs.Clear()
            ' 不再需要保存空日志，因为每个会话使用不同的文件

            UtilityModule.ShowInfo(Me, My.Resources.MsgLogsCleared, My.Resources.TitleOperationSuccess)
        Catch ex As Exception
            UtilityModule.ShowError(Me, String.Format(My.Resources.MsgClearLogsFailed, ex.Message))
        End Try
    End Sub

    Private Sub BtnExportLogs_Click() Handles BtnExportLogs.Click
        If _logs.Count = 0 Then
            UtilityModule.ShowWarning(Me, My.Resources.MsgNoLogsToExport, My.Resources.TitleHint)
            Return
        End If

        Dim saveDialog As New Microsoft.Win32.SaveFileDialog With {
            .Filter = My.Resources.FilterXmlFiles,
            .FileName = $"mcp_logs_{DateTime.Now:yyyyMMdd_HHmmss}.xml",
            .Title = My.Resources.TitleExportLogs
        }

        If saveDialog.ShowDialog() Then
            Try
                PersistenceModule.ExportLogs(saveDialog.FileName, _logs)
                UtilityModule.ShowInfo(Me, String.Format(My.Resources.MsgLogsExported, saveDialog.FileName), My.Resources.TitleExportSuccess)
            Catch ex As Exception
                UtilityModule.ShowError(Me, String.Format(My.Resources.MsgExportLogsFailed, ex.Message))
            End Try
        End If
    End Sub

    Public Sub LogOperation(operation As String, result As String, details As String)
        Dim entry As New LogEntry With {
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
            ' 确保日志被保存到以应用启动时间命名的文件
            If _logs.Count > 0 Then
                PersistenceModule.SaveLogsToAppStartupFile(_logs.ToList(), _appStartTime)
            End If
        Catch ex As Exception
            ' 忽略关闭时保存日志的错误
        End Try
    End Sub
End Class