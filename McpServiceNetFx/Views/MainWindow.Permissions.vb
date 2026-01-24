Imports System.Collections.ObjectModel
Imports McpServiceNetFx.Models

Partial Public Class MainWindow
    Private _permissionItems As New ObservableCollection(Of PermissionItem)()
    Private _denyPolicies As New List(Of PathPermissionPolicy)
    Private _allowPolicies As New List(Of PathPermissionPolicy)
    Private _allowPolicyItems As New ObservableCollection(Of PathPermissionPolicy)()
    Private _denyPolicyItems As New ObservableCollection(Of PathPermissionPolicy)()

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
            ' 保存功能权限配置
            PersistenceModule.SavePermissions(_permissionItems)

            ' 保存路径策略配置
            Dim allPolicies = New List(Of PathPermissionPolicy)()
            allPolicies.AddRange(_allowPolicyItems)
            allPolicies.AddRange(_denyPolicyItems)
            PersistenceModule.SavePathPolicies(allPolicies)

            UtilityModule.ShowInfo(Me, My.Resources.MsgSaveConfigAndPoliciesSuccess, My.Resources.TitleSaveSuccess)
            LogOperation(My.Resources.LogPermissions, My.Resources.LogCompleted, String.Format(My.Resources.LogConfigAndPoliciesSaved, _permissionItems.Count, allPolicies.Count))
        Catch ex As Exception
            UtilityModule.ShowError(Me, String.Format(My.Resources.MsgSaveConfigAndPoliciesFailed, ex.Message))
        End Try
    End Sub

    Private Sub BtnReloadPermissions_Click() Handles BtnReloadPermissions.Click
        LoadPermissions()
        LoadPathPolicies()
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
            Case PermissionLevel.AlwaysAsk
                ' AlwaysAsk 模式下，总是弹基础对话框（不使用路径策略）
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

    ''' <summary>
    ''' 检查文件权限（增强版本，支持路径策略）
    ''' </summary>
    Public Function CheckFilePermission(featureName As String, operationDescription As String, filePath As String, accessType As FileAccessType) As Boolean Implements IMcpPermissionHandler.CheckFilePermission
        Dim permission = GetPermission(featureName)
        LogOperation(My.Resources.LogPermissionCheck, My.Resources.LogGetPermission, String.Format(My.Resources.LogFeaturePermissionValue, featureName, permission))

        Select Case permission
            Case PermissionLevel.Allow
                LogOperation(featureName, My.Resources.LogAllowed, operationDescription)
                Return True

            Case PermissionLevel.Deny
                LogOperation(featureName, My.Resources.LogDenied, operationDescription)
                Return False

            Case PermissionLevel.AlwaysAsk
                ' AlwaysAsk 模式：跳过路径策略，每次弹基础对话框
                LogOperation(featureName, My.Resources.LogAskUser, operationDescription)
                Dim message = String.Format(My.Resources.MsgPermissionRequest, Environment.NewLine, featureName, operationDescription)
                Dim result = UtilityModule.ShowConfirmModal(Me, message, My.Resources.TitlePermissionConfirm)

                If result Then
                    LogOperation(featureName, My.Resources.LogUserAllowed, operationDescription)
                Else
                    LogOperation(featureName, My.Resources.LogUserDenied, operationDescription)
                End If

                Return result

            Case PermissionLevel.Ask
                ' Ask 模式：检查路径策略
                Return CheckPathPoliciesOrAsk(featureName, operationDescription, filePath, accessType)

            Case Else
                LogOperation(featureName, My.Resources.LogUnknownPermission, String.Format(My.Resources.LogPermissionValueDetails, permission))
                Return False
        End Select
    End Function

    ''' <summary>
    ''' 检查路径策略或询问用户
    ''' Ask 模式下使用路径策略进行自动应答
    ''' </summary>
    Private Function CheckPathPoliciesOrAsk(featureName As String, operationDescription As String, filePath As String, accessType As FileAccessType) As Boolean
        ' 1. 首先检查拒绝列表（优先级最高）
        For Each policy In _denyPolicies
            If policy.AppliesTo(accessType) AndAlso policy.Matches(filePath) Then
                LogOperation(featureName, My.Resources.LogDenied, String.Format(My.Resources.LogPathPolicyMatch, policy.PolicyType, policy.Pattern))
                Return False
            End If
        Next

        ' 2. 然后检查允许列表
        For Each policy In _allowPolicies
            If policy.AppliesTo(accessType) AndAlso policy.Matches(filePath) Then
                LogOperation(featureName, My.Resources.LogAllowed, String.Format(My.Resources.LogPathPolicyMatch, policy.PolicyType, policy.Pattern))
                Return True
            End If
        Next

        ' 3. 未匹配任何策略，弹出增强对话框
        LogOperation(featureName, My.Resources.LogAskUser, operationDescription)
        Return ShowFilePermissionDialog(featureName, operationDescription, filePath)
    End Function

    ''' <summary>
    ''' 显示文件权限确认对话框
    ''' </summary>
    Private Function ShowFilePermissionDialog(featureName As String, operationDescription As String, filePath As String) As Boolean
        Try
            Dim dialog As New PermissionConfirmDialog()
            dialog.SetContent(featureName, operationDescription, filePath)
            dialog.Owner = Me

            Dim showDialog = dialog.ShowDialog()

            If showDialog.HasValue Then
                Dim result = dialog.GetResult()

                Select Case result
                    Case PermissionConfirmResult.Allow
                        LogOperation(featureName, My.Resources.LogUserAllowed, operationDescription)
                        Return True

                    Case PermissionConfirmResult.Deny
                        LogOperation(featureName, My.Resources.LogUserDenied, operationDescription)
                        Return False

                    Case PermissionConfirmResult.AllowWithPolicy
                        ' 添加允许策略
                        Dim pattern = dialog.GetPolicyPattern()
                        AddPathPolicy(PathPolicyType.Allow, FileAccessType.ReadWrite, pattern)
                        LogOperation(featureName, My.Resources.LogUserAllowed, String.Format(My.Resources.LogPathPolicyAdded, "Allow", pattern))
                        Return True

                    Case PermissionConfirmResult.DenyWithPolicy
                        ' 添加拒绝策略
                        Dim pattern = dialog.GetPolicyPattern()
                        AddPathPolicy(PathPolicyType.Deny, FileAccessType.ReadWrite, pattern)
                        LogOperation(featureName, My.Resources.LogUserDenied, String.Format(My.Resources.LogPathPolicyAdded, "Deny", pattern))
                        Return False
                End Select
            End If

            ' 默认拒绝
            Return False
        Catch ex As Exception
            LogOperation(featureName, My.Resources.LogFailed, String.Format(My.Resources.LogPermissionDialogFailed, ex.Message))
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 添加路径策略
    ''' </summary>
    Public Sub AddPathPolicy(policyType As PathPolicyType, fileAccess As FileAccessType, pattern As String)
        Try
            Dim policy = New PathPermissionPolicy(policyType, fileAccess, pattern)

            If policyType = PathPolicyType.Allow Then
                ' 检查是否已存在相同策略
                If Not _allowPolicies.Any(Function(p) p.Pattern = pattern AndAlso p.FileAccess = fileAccess) Then
                    _allowPolicies.Add(policy)
                End If
            Else
                ' 检查是否已存在相同策略
                If Not _denyPolicies.Any(Function(p) p.Pattern = pattern AndAlso p.FileAccess = fileAccess) Then
                    _denyPolicies.Add(policy)
                End If
            End If

            LogOperation(My.Resources.LogPermissionCheck, My.Resources.LogCompleted, String.Format(My.Resources.LogPathPolicyAdded, policyType, pattern))
        Catch ex As Exception
            LogOperation(My.Resources.LogPermissionCheck, My.Resources.LogFailed, String.Format(My.Resources.LogAddPathPolicyFailed, ex.Message))
        End Try
    End Sub

    ''' <summary>
    ''' 加载路径策略配置
    ''' </summary>
    Private Sub LoadPathPolicies()
        Try
            Dim loadedPolicies = PersistenceModule.LoadPathPolicies()
            _allowPolicies.Clear()
            _denyPolicies.Clear()
            _allowPolicyItems.Clear()
            _denyPolicyItems.Clear()

            For Each policy In loadedPolicies
                If policy.PolicyType = PathPolicyType.Allow Then
                    _allowPolicies.Add(policy)
                    _allowPolicyItems.Add(policy)
                Else
                    _denyPolicies.Add(policy)
                    _denyPolicyItems.Add(policy)
                End If
            Next

            DgAllowPolicies.ItemsSource = _allowPolicyItems
            DgDenyPolicies.ItemsSource = _denyPolicyItems
            LogOperation(My.Resources.LogPermissions, My.Resources.LogCompleted, String.Format(My.Resources.LogPermissionsLoaded, loadedPolicies.Count))
        Catch ex As Exception
            UtilityModule.ShowError(Me, String.Format(My.Resources.MsgLoadPermissionsFailed, ex.Message))
        End Try
    End Sub

    ''' <summary>
    ''' 添加策略按钮点击事件
    ''' </summary>
    Private Sub BtnAddPolicy_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim selectedTab = TabPathPolicies.SelectedIndex
            Dim newPolicy As PathPermissionPolicy

            If selectedTab = 0 Then
                ' 允许列表
                newPolicy = New PathPermissionPolicy(PathPolicyType.Allow, FileAccessType.ReadWrite, String.Empty)
                AddPathPolicy(PathPolicyType.Allow, FileAccessType.ReadWrite, String.Empty)
                _allowPolicyItems.Add(newPolicy)
            Else
                ' 拒绝列表
                newPolicy = New PathPermissionPolicy(PathPolicyType.Deny, FileAccessType.ReadWrite, String.Empty)
                AddPathPolicy(PathPolicyType.Deny, FileAccessType.ReadWrite, String.Empty)
                _denyPolicyItems.Add(newPolicy)
            End If

            LogOperation(My.Resources.LogPermissionCheck, My.Resources.LogCompleted, "New policy added")
        Catch ex As Exception
            UtilityModule.ShowError(Me, String.Format(My.Resources.MsgSavePermissionsFailed, ex.Message))
        End Try
    End Sub

    ''' <summary>
    ''' 了解更多按钮点击事件
    ''' </summary>
    Private Sub BtnLearnMore_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim learnMoreUrl = My.Resources.LearnMore_Url
            Process.Start(New ProcessStartInfo(learnMoreUrl) With {
                .UseShellExecute = True,
                .Verb = "open"
            })
            LogOperation("UI", My.Resources.LogCompleted, "Opened Learn More link")
        Catch ex As Exception
            UtilityModule.ShowError(Me, String.Format(My.Resources.MsgCannotOpenHelpDoc, ex.Message))
        End Try
    End Sub

End Class