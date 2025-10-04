Imports System.Collections.ObjectModel
Imports System.ComponentModel

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

            DgPermissions.ItemsSource = _permissionItems
            SetupPermissionComboBox()
            LogOperation("权限加载", "完成", $"共加载 {_permissionItems.Count} 个权限配置")
        Catch ex As Exception
            UtilityModule.ShowError(Me, $"加载权限配置失败: {ex.Message}")
        End Try
    End Sub

    Private Sub SetupPermissionComboBox()
        Dim permissionValues = New List(Of PermissionLevel) From {
            PermissionLevel.Allow,
            PermissionLevel.Ask,
            PermissionLevel.Deny
        }

        PermissionColumn.ItemsSource = permissionValues
    End Sub

    Private Sub BtnAllowAll_Click() Handles BtnAllowAll.Click
        SetAllPermissions(PermissionLevel.Allow)
    End Sub

    Private Sub BtnAskAll_Click() Handles BtnAskAll.Click
        SetAllPermissions(PermissionLevel.Ask)
    End Sub

    Private Sub SaveCurrentPermissions()
        Try
            PersistenceModule.SavePermissions(_permissionItems)
            LogOperation("权限保存", "成功", $"权限配置已保存到文件")
        Catch ex As Exception
            LogOperation("权限保存", "失败", ex.Message)
            Throw
        End Try
    End Sub

    Private Sub SetAllPermissions(permission As PermissionLevel)
        ' 更新所有权限项
        For Each item In _permissionItems
            item.Permission = permission
        Next

        LogOperation("权限设置", "批量设置完成", $"所有权限已设置为: {permission}")
    End Sub

    Private Sub BtnSavePermissions_Click() Handles BtnSavePermissions.Click
        Try
            PersistenceModule.SavePermissions(_permissionItems)
            UtilityModule.ShowInfo(Me, "权限配置已保存", "保存成功")
        Catch ex As Exception
            UtilityModule.ShowError(Me, $"保存权限配置失败: {ex.Message}")
        End Try
    End Sub

    Private Sub BtnReloadPermissions_Click() Handles BtnReloadPermissions.Click
        LoadPermissions()
    End Sub

    Public Function GetPermission(featureName As String) As PermissionLevel
        Dim permissionItem = _permissionItems.FirstOrDefault(Function(p) p.FeatureName = featureName)
        If permissionItem Is Nothing Then
            LogOperation("权限检查", "未找到配置", $"功能 '{featureName}' 使用默认权限 Ask")
            Return PermissionLevel.Ask ' 默认为询问
        Else
            LogOperation("权限检查", "找到配置", $"功能 '{featureName}' 权限: {permissionItem.Permission}")
            Return permissionItem.Permission
        End If
    End Function

    Public Function CheckPermission(featureName As String, operationDescription As String) As Boolean Implements IMcpPermissionHandler.CheckPermission
        Dim permission = GetPermission(featureName)
        LogOperation("权限检查", "获取权限", $"功能: {featureName}, 权限值: {permission}")

        Select Case permission
            Case PermissionLevel.Allow
                LogOperation(featureName, "已允许", operationDescription)
                Return True
            Case PermissionLevel.Deny
                LogOperation(featureName, "已拒绝", operationDescription)
                Return False
            Case PermissionLevel.Ask
                LogOperation(featureName, "询问用户", operationDescription)
                Dim message = $"是否允许执行以下操作？{Environment.NewLine}{Environment.NewLine}功能: {featureName}{Environment.NewLine}描述: {operationDescription}"
                Dim result = UtilityModule.ShowConfirmModal(Me, message, "权限确认")

                If result Then
                    LogOperation(featureName, "用户允许", operationDescription)
                Else
                    LogOperation(featureName, "用户拒绝", operationDescription)
                End If

                Return result
            Case Else
                LogOperation(featureName, "未知权限", $"权限值: {permission}")
                Return False
        End Select
    End Function

End Class