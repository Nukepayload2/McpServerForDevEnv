Imports System
Imports Microsoft.VisualStudio.Shell
Imports Microsoft.VisualStudio.Shell.Settings
Imports McpServiceNetFx.Models
Imports System.Collections.Generic
Imports Microsoft.VisualStudio.Settings

Namespace Helpers
    ''' <summary>
    ''' 使用Visual Studio内置的ShellSettingsManager进行权限持久化的辅助类
    ''' </summary>
    Public Class SettingsPersistenceHelper
        Private Const CollectionName As String = "McpPermissions"
        Private ReadOnly _settingsManager As ShellSettingsManager

        Public Sub New(serviceProvider As IServiceProvider)
            _settingsManager = New ShellSettingsManager(serviceProvider)
        End Sub

        ''' <summary>
        ''' 保存权限配置到Visual Studio用户设置
        ''' </summary>
        ''' <param name="permissions">权限列表</param>
        Public Sub SavePermissions(permissions As IEnumerable(Of PermissionItem))
            Try
                Dim settingsStore = _settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings)

                ' 创建集合（如果不存在）
                If Not settingsStore.CollectionExists(CollectionName) Then
                    settingsStore.CreateCollection(CollectionName)
                End If

                ' 只保存每个工具的权限级别
                For Each permission In permissions
                    Dim propertyName = permission.FeatureName
                    settingsStore.SetString(CollectionName, propertyName, permission.Permission.ToString())
                Next

            Catch ex As Exception
                Throw New Exception("使用Visual Studio设置保存权限配置失败", ex)
            End Try
        End Sub

        ''' <summary>
        ''' 从Visual Studio用户设置加载权限配置
        ''' </summary>
        ''' <returns>权限列表</returns>
        Public Function LoadPermissions() As Dictionary(Of String, PermissionLevel)
            Try
                Dim settingsStore = _settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings)
                Dim permissions As New Dictionary(Of String, PermissionLevel)()

                ' 检查集合是否存在
                If Not settingsStore.CollectionExists(CollectionName) Then
                    Return permissions ' 返回空字典
                End If

                ' 获取所有属性名（工具名称）
                Dim propertyNames = settingsStore.GetPropertyNames(CollectionName)

                ' 解析权限项
                For Each featureName In propertyNames
                    Dim permissionValue = settingsStore.GetString(CollectionName, featureName)

                    ' 解析权限级别
                    Dim parsedPermission As PermissionLevel
                    If [Enum].TryParse(Of PermissionLevel)(permissionValue, parsedPermission) Then
                        permissions(featureName) = parsedPermission
                    End If
                Next

                Return permissions

            Catch ex As Exception
                ' 如果加载失败，返回空字典
                Return New Dictionary(Of String, PermissionLevel)()
            End Try
        End Function

        ''' <summary>
        ''' 删除权限配置
        ''' </summary>
        Public Sub DeletePermissions()
            Try
                Dim settingsStore = _settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings)

                If settingsStore.CollectionExists(CollectionName) Then
                    settingsStore.DeleteCollection(CollectionName)
                End If

            Catch ex As Exception
                Throw New Exception("删除Visual Studio设置中的权限配置失败", ex)
            End Try
        End Sub

        ''' <summary>
        ''' 检查权限配置是否存在
        ''' </summary>
        ''' <returns>是否存在权限配置</returns>
        Public Function PermissionsExist() As Boolean
            Try
                Dim settingsStore = _settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings)
                Return settingsStore.CollectionExists(CollectionName)
            Catch ex As Exception
                Return False
            End Try
        End Function
    End Class
End Namespace