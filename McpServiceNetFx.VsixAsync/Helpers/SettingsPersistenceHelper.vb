Imports System
Imports Microsoft.VisualStudio.Shell.Settings
Imports System.Collections.Generic
Imports Microsoft.VisualStudio.Settings

Namespace Helpers
    ''' <summary>
    ''' 使用Visual Studio内置的ShellSettingsManager进行权限持久化的辅助类
    ''' </summary>
    Public Class SettingsPersistenceHelper
        Private Const PermissionsCollectionName As String = "McpPermissions"
        Private Const ConfigurationCollectionName As String = "McpConfiguration"
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

                If Not settingsStore.CollectionExists(PermissionsCollectionName) Then
                    settingsStore.CreateCollection(PermissionsCollectionName)
                End If

                ' 只保存每个工具的权限级别
                For Each permission In permissions
                    Dim propertyName = permission.FeatureName
                    settingsStore.SetString(PermissionsCollectionName, propertyName, permission.Permission.ToString())
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

                If Not settingsStore.CollectionExists(PermissionsCollectionName) Then
                    Return permissions
                End If

                Dim propertyNames = settingsStore.GetPropertyNames(PermissionsCollectionName)

                For Each featureName In propertyNames
                    Dim permissionValue = settingsStore.GetString(PermissionsCollectionName, featureName)

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
        ''' 保存服务器配置到Visual Studio用户设置
        ''' </summary>
        ''' <param name="config">服务器配置</param>
        Public Sub SaveServerConfiguration(config As ServerConfiguration)
            Try
                Dim settingsStore = _settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings)

                If Not settingsStore.CollectionExists(ConfigurationCollectionName) Then
                    settingsStore.CreateCollection(ConfigurationCollectionName)
                End If

                ' 直接保存端口号
                settingsStore.SetInt32(ConfigurationCollectionName, "Port", config.Port)

            Catch ex As Exception
                Throw New Exception("使用Visual Studio设置保存服务器配置失败", ex)
            End Try
        End Sub

        ''' <summary>
        ''' 从Visual Studio用户设置加载服务器配置
        ''' </summary>
        ''' <returns>服务器配置</returns>
        Public Function LoadServerConfiguration() As ServerConfiguration
            Try
                Dim settingsStore = _settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings)

                If Not settingsStore.CollectionExists(ConfigurationCollectionName) Then
                    Return New ServerConfiguration()
                End If

                If settingsStore.PropertyExists(ConfigurationCollectionName, "Port") Then
                    Dim port = settingsStore.GetInt32(ConfigurationCollectionName, "Port")
                    Return New ServerConfiguration() With {.Port = port}
                Else
                    Return New ServerConfiguration()
                End If

            Catch ex As Exception
                ' 如果加载失败，返回默认配置
                Return New ServerConfiguration()
            End Try
        End Function

        ''' <summary>
        ''' 删除权限配置
        ''' </summary>
        Public Sub DeletePermissions()
            Try
                Dim settingsStore = _settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings)

                If settingsStore.CollectionExists(PermissionsCollectionName) Then
                    settingsStore.DeleteCollection(PermissionsCollectionName)
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
                Return settingsStore.CollectionExists(PermissionsCollectionName)
            Catch ex As Exception
                Return False
            End Try
        End Function
    End Class
End Namespace