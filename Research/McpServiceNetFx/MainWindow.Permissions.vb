Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Collections.Specialized

Partial Public Class MainWindow
    Private _permissions As New List(Of PersistenceModule.FeaturePermission)()
    Private _permissionItems As New ObservableCollection(Of PermissionItem)()

    Private Class PermissionItem
        Implements INotifyPropertyChanged

        Public Property FeatureName As String
        Public Property Description As String
        Private _permission As PersistenceModule.PermissionLevel

        Public Property Permission As PersistenceModule.PermissionLevel
            Get
                Return _permission
            End Get
            Set
                _permission = Value
                OnPropertyChanged(NameOf(Permission))
            End Set
        End Property

        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

        Protected Overridable Sub OnPropertyChanged(name As String)
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(name))
        End Sub
    End Class

    Private Sub LoadPermissions()
        Try
            _permissions = PersistenceModule.LoadPermissions()
            _permissionItems.Clear()

            For Each permission In _permissions
                _permissionItems.Add(New PermissionItem With {
                    .FeatureName = permission.FeatureName,
                    .Description = permission.Description,
                    .Permission = permission.Permission
                })
            Next

            DgPermissions.ItemsSource = _permissionItems
            SetupPermissionComboBox()
        Catch ex As Exception
            UtilityModule.ShowError(Me, $"加载权限配置失败: {ex.Message}")
        End Try
    End Sub

    Private Sub SetupPermissionComboBox()
        Dim permissionValues = New List(Of PersistenceModule.PermissionLevel) From {
            PersistenceModule.PermissionLevel.Allow,
            PersistenceModule.PermissionLevel.Ask,
            PersistenceModule.PermissionLevel.Deny
        }

        PermissionColumn.ItemsSource = permissionValues
    End Sub

    Private Sub BtnAllowAll_Click() Handles BtnAllowAll.Click
        SetAllPermissions(PersistenceModule.PermissionLevel.Allow)
    End Sub

    Private Sub BtnAskAll_Click() Handles BtnAskAll.Click
        SetAllPermissions(PersistenceModule.PermissionLevel.Ask)
    End Sub

    Private Sub BtnCustom_Click() Handles BtnCustom.Click
        ' 自定义模式，不需要特殊处理，用户可以手动调整每个权限
    End Sub

    Private Sub SetAllPermissions(permission As PersistenceModule.PermissionLevel)
        For Each item In _permissionItems
            item.Permission = permission
        Next
    End Sub

    Private Sub BtnSavePermissions_Click() Handles BtnSavePermissions.Click
        Try
            Dim updatedPermissions = New List(Of PersistenceModule.FeaturePermission)()

            For Each item In _permissionItems
                updatedPermissions.Add(New PersistenceModule.FeaturePermission With {
                    .FeatureName = item.FeatureName,
                    .Description = item.Description,
                    .Permission = item.Permission
                })
            Next

            PersistenceModule.SavePermissions(updatedPermissions)
            _permissions = updatedPermissions

            UtilityModule.ShowInfo(Me, "权限配置已保存", "保存成功")
        Catch ex As Exception
            UtilityModule.ShowError(Me, $"保存权限配置失败: {ex.Message}")
        End Try
    End Sub

    Private Sub BtnReloadPermissions_Click() Handles BtnReloadPermissions.Click
        LoadPermissions()
    End Sub

    Public Function GetPermission(featureName As String) As PersistenceModule.PermissionLevel
        Dim permission = _permissions.FirstOrDefault(Function(p) p.FeatureName = featureName)
        If permission.FeatureName IsNot Nothing Then
            Return permission.Permission
        Else
            Return PersistenceModule.PermissionLevel.Ask ' 默认为询问
        End If
    End Function

    Public Function CheckPermission(featureName As String, operationDescription As String) As Boolean
        Dim permission = GetPermission(featureName)

        Select Case permission
            Case PersistenceModule.PermissionLevel.Allow
                LogOperation(featureName, "已允许", operationDescription)
                Return True
            Case PersistenceModule.PermissionLevel.Deny
                LogOperation(featureName, "已拒绝", operationDescription)
                Return False
            Case PersistenceModule.PermissionLevel.Ask
                Dim message = $"是否允许执行以下操作？{Environment.NewLine}{Environment.NewLine}功能: {featureName}{Environment.NewLine}描述: {operationDescription}"
                Dim result = UtilityModule.ShowConfirm(Me, message, "权限确认")

                If result Then
                    LogOperation(featureName, "用户允许", operationDescription)
                Else
                    LogOperation(featureName, "用户拒绝", operationDescription)
                End If

                Return result
            Case Else
                Return False
        End Select
    End Function

    End Class