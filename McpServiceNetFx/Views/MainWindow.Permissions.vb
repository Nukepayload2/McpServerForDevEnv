Imports System.Collections.ObjectModel

Partial Public Class MainWindow
    Private _permissionItems As New ObservableCollection(Of PermissionItem)()

    Private Sub LoadPermissions()
        Try
            Dim loadedPermissions = PersistenceModule.LoadPermissions()
            _permissionItems.Clear()

            ' 添加已加载的权限
            For Each permission In loadedPermissions
                _permissionItems.Add(New PermissionItem With {
                    .FeatureName = permission.FeatureName,
                    .Description = permission.Description,
                    .Permission = permission.Permission
                })
            Next

            ' 同步工具管理器中的权限（如果工具管理器已初始化）
            SyncPermissionsWithToolManager()

            DgPermissions.ItemsSource = _permissionItems
            LogOperation(My.Resources.LogPermissions, My.Resources.LogCompleted, String.Format(My.Resources.LogPermissionsLoaded, _permissionItems.Count))
        Catch ex As Exception
            UtilityModule.ShowError(Me, String.Format(My.Resources.MsgLoadPermissionsFailed, ex.Message))
        End Try
    End Sub

    ''' <summary>
    ''' 同步工具管理器中的权限配置
    ''' 确保所有已注册的工具都有对应的权限项
    ''' </summary>
    Private Sub SyncPermissionsWithToolManager()
        Try
            ' 获取工具管理器实例（如果已创建）
            Dim toolManager = GetCurrentToolManager()
            If toolManager Is Nothing Then
                LogOperation(My.Resources.LogPermissionSync, My.Resources.LogSkipped, My.Resources.LogToolManagerNotInitialized)
                Return
            End If

            ' 获取工具管理器中的默认权限配置
            Dim defaultPermissions = toolManager.GetDefaultPermissions()
            If defaultPermissions Is Nothing OrElse defaultPermissions.Count = 0 Then
                LogOperation(My.Resources.LogPermissionSync, My.Resources.LogSkipped, My.Resources.LogNoPermissionConfig)
                Return
            End If

            ' 同步权限：添加缺失的工具权限项
            Dim addedCount = 0
            For Each defaultPermission In defaultPermissions
                Dim existingPermission = _permissionItems.FirstOrDefault(Function(p) p.FeatureName = defaultPermission.FeatureName)
                If existingPermission Is Nothing Then
                    ' 添加新工具的权限项
                    _permissionItems.Add(New PermissionItem With {
                        .FeatureName = defaultPermission.FeatureName,
                        .Description = defaultPermission.Description,
                        .Permission = defaultPermission.Permission
                    })
                    addedCount += 1
                Else
                    ' 更新现有权限项的描述（以防描述有变化）
                    existingPermission.Description = defaultPermission.Description
                End If
            Next

            ' 清理已不存在的工具的权限项（可选）
            ' Dim removedPermissions = _permissionItems.Where(Function(p)
            '     Not defaultPermissions.Any(Function(dp) dp.FeatureName = p.FeatureName)).ToList()
            ' For Each removed In removedPermissions
            '     _permissionItems.Remove(removed)
            ' Next

            LogOperation(My.Resources.LogPermissionSync, My.Resources.LogCompleted, String.Format(My.Resources.LogAddedNewToolPermissions, addedCount))
        Catch ex As Exception
            LogOperation(My.Resources.LogPermissionSync, My.Resources.LogFailed, ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' 获取当前的工具管理器实例
    ''' </summary>
    ''' <returns>工具管理器实例，如果未初始化则返回 Nothing</returns>
    Private Function GetCurrentToolManager() As VisualStudioToolManager
        Try
            ' 直接使用主窗口的工具管理器实例
            If _toolManager IsNot Nothing Then
                Return _toolManager
            Else
                LogOperation(My.Resources.LogGetToolManager, My.Resources.LogSkipped, My.Resources.LogToolManagerNotInitialized)
                Return Nothing
            End If
        Catch ex As Exception
            LogOperation(My.Resources.LogGetToolManager, My.Resources.LogFailed, ex.Message)
            Return Nothing
        End Try
    End Function

    Private Sub BtnAllowAll_Click() Handles BtnAllowAll.Click
        SetAllPermissions(PermissionLevel.Allow)
    End Sub

    Private Sub BtnAskAll_Click() Handles BtnAskAll.Click
        SetAllPermissions(PermissionLevel.Ask)
    End Sub

    Private Sub SetAllPermissions(permission As PermissionLevel)
        ' 更新所有权限项
        For Each item In _permissionItems
            item.Permission = permission
        Next

        LogOperation(My.Resources.LogPermissionSet, My.Resources.LogCompleted, String.Format(My.Resources.LogBatchSetCompleted, permission))
    End Sub

    Private Sub BtnSavePermissions_Click() Handles BtnSavePermissions.Click
        Try
            PersistenceModule.SavePermissions(_permissionItems)
            UtilityModule.ShowInfo(Me, My.Resources.MsgSavePermissionsSuccess, My.Resources.TitleSaveSuccess)
        Catch ex As Exception
            UtilityModule.ShowError(Me, String.Format(My.Resources.MsgSavePermissionsFailed, ex.Message))
        End Try
    End Sub

    Private Sub BtnReloadPermissions_Click() Handles BtnReloadPermissions.Click
        LoadPermissions()
    End Sub

    Public Function GetPermission(featureName As String) As PermissionLevel
        Dim permissionItem = _permissionItems.FirstOrDefault(Function(p) p.FeatureName = featureName)
        If permissionItem Is Nothing Then
            LogOperation(My.Resources.LogPermissionCheck, My.Resources.LogFailed, String.Format(My.Resources.LogPermissionConfigNotFound, featureName))
            Return PermissionLevel.Ask ' 默认为询问
        Else
            LogOperation(My.Resources.LogPermissionCheck, My.Resources.LogCompleted, String.Format(My.Resources.LogPermissionConfigFound, featureName, permissionItem.Permission))
            Return permissionItem.Permission
        End If
    End Function

    Public Function CheckPermission(featureName As String, operationDescription As String) As Boolean Implements IMcpPermissionHandler.CheckPermission
        Dim permission = GetPermission(featureName)
        LogOperation(My.Resources.LogPermissionCheck, My.Resources.LogGetPermission, String.Format(My.Resources.LogFeaturePermissionValue, featureName, permission))

        Select Case permission
            Case PermissionLevel.Allow
                LogOperation(featureName, My.Resources.LogAllowed, operationDescription)
                Return True
            Case PermissionLevel.Deny
                LogOperation(featureName, My.Resources.LogDenied, operationDescription)
                Return False
            Case PermissionLevel.Ask
                LogOperation(featureName, My.Resources.LogAskUser, operationDescription)
                Dim message = String.Format(My.Resources.MsgPermissionRequest, Environment.NewLine, featureName, operationDescription)
                Dim result = UtilityModule.ShowConfirmModal(Me, message, My.Resources.TitlePermissionConfirm)

                If result Then
                    LogOperation(featureName, My.Resources.LogUserAllowed, operationDescription)
                Else
                    LogOperation(featureName, My.Resources.LogUserDenied, operationDescription)
                End If

                Return result
            Case Else
                LogOperation(featureName, My.Resources.LogUnknownPermission, String.Format(My.Resources.LogPermissionValueDetails, permission))
                Return False
        End Select
    End Function

End Class