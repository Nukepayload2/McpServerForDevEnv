Imports System.IO

Public Module PersistenceModule
    Private ReadOnly LocalAppDataPath As String = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
    Private ReadOnly AppDataFolder As String = Path.Combine(LocalAppDataPath, GetType(PersistenceModule).Assembly.GetName().Name)
    Private ReadOnly LogsFolder As String = Path.Combine(AppDataFolder, "logs")

    Private Function EnsureAppDataFolder() As String
        If Not Directory.Exists(AppDataFolder) Then
            Directory.CreateDirectory(AppDataFolder)
        End If
        Return AppDataFolder
    End Function

    Private Function EnsureLogsFolder() As String
        If Not Directory.Exists(LogsFolder) Then
            Directory.CreateDirectory(LogsFolder)
        End If
        Return LogsFolder
    End Function

    Public Sub SavePermissions(permissions As IEnumerable(Of PermissionItem))
        Try
            Dim folder = EnsureAppDataFolder()
            Dim filePath = Path.Combine(folder, "permissions.xml")

            Dim doc = <?xml version="1.0" encoding="utf-8" standalone="yes"?>
                      <Permissions>
                          <%= From p In permissions
                              Select <Permission
                                         FeatureName=<%= p.FeatureName %>
                                         Description=<%= p.Description %>
                                         Permission=<%= p.Permission.ToString() %>/> %>
                      </Permissions>

            doc.Save(filePath)
        Catch ex As Exception
            Throw New Exception("保存权限配置失败", ex)
        End Try
    End Sub

    Public Function LoadPermissions() As List(Of PermissionItem)
        Try
            Dim folder = EnsureAppDataFolder()
            Dim filePath = Path.Combine(folder, "permissions.xml")

            If Not File.Exists(filePath) Then
                ' 权限文件不存在时返回空列表，工具权限将在 MCP 服务启动时自动同步
                Return New List(Of PermissionItem)()
            End If

            Dim doc = XDocument.Load(filePath)
            Dim permissions = New List(Of PermissionItem)()

            For Each element In doc.Root.Elements("Permission")
                Dim permission As New PermissionItem With {
                    .FeatureName = element.@FeatureName,
                    .Description = element.@Description
                }

                Dim permissionValue = element.@Permission
                Dim parsedPermission As PermissionLevel
                If [Enum].TryParse(Of PermissionLevel)(permissionValue, parsedPermission) Then
                    permission.Permission = parsedPermission
                Else
                    permission.Permission = PermissionLevel.Ask ' 默认为询问
                End If

                permissions.Add(permission)
            Next

            Return permissions
        Catch ex As Exception
            ' 如果加载失败，返回空列表，工具权限将在 MCP 服务启动时自动同步
            Return New List(Of PermissionItem)()
        End Try
    End Function

    ''' <summary>
    ''' 获取所有工具的默认权限配置
    ''' 注意：此方法已废弃，请使用 GetDefaultPermissionsFromToolManager 替代
    ''' </summary>
    ''' <returns>权限配置列表</returns>
    <Obsolete("此方法已废弃，请使用 GetDefaultPermissionsFromToolManager 替代")>
    Private Function GetDefaultPermissions() As List(Of PermissionItem)
        ' 返回空列表，因为 KnownTools 已被废弃
        Return New List(Of PermissionItem)()
    End Function

    ''' <summary>
    ''' 从工具管理器获取默认权限配置
    ''' </summary>
    ''' <param name="toolManager">工具管理器实例</param>
    ''' <returns>权限配置列表</returns>
    Public Function GetDefaultPermissionsFromToolManager(toolManager As VisualStudioToolManager) As List(Of PermissionItem)
        If toolManager Is Nothing Then
            ' 没有工具管理器时返回空列表
            Return New List(Of PermissionItem)()
        End If

        Try
            Return toolManager.GetDefaultPermissions()
        Catch ex As Exception
            ' 如果从工具管理器获取权限失败，返回空列表
            Return New List(Of PermissionItem)()
        End Try
    End Function

    Public Sub SaveLogsToAppStartupFile(logs As IEnumerable(Of LogEntry), appStartTime As DateTime)
        Try
            Dim folder = EnsureLogsFolder()
            Dim fileName = $"mcp_logs_{appStartTime:yyyyMMdd_HHmmss}.xml"
            Dim filePath = Path.Combine(folder, fileName)

            Dim doc = <?xml version="1.0" encoding="utf-8" standalone="yes"?>
                      <Logs AppStartTime=<%= appStartTime.ToString("yyyy-MM-dd HH:mm:ss") %>
                          SessionStart=<%= appStartTime.ToString("yyyy-MM-dd HH:mm:ss") %>
                          Count=<%= logs.Count() %>>
                          <%= From log In logs
                              Select <Log Timestamp=<%= log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") %>
                                         Operation=<%= log.Operation %>
                                         Result=<%= log.Result %>
                                         Details=<%= log.Details %>/> %>
                      </Logs>

            doc.Save(filePath)
        Catch ex As Exception
            Throw New Exception("保存日志失败", ex)
        End Try
    End Sub

    Public Sub ExportLogs(filePath As String, logs As IEnumerable(Of LogEntry))
        Try
            Dim doc = <?xml version="1.0" encoding="utf-8" standalone="yes"?>
                      <Logs ExportDate=<%= DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") %>
                          Count=<%= logs.Count() %>>
                          <%= From log In logs
                              Select <Log Timestamp=<%= log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") %>
                                         Operation=<%= log.Operation %>
                                         Result=<%= log.Result %>
                                         Details=<%= log.Details %>/> %>
                      </Logs>

            doc.Save(filePath)
        Catch ex As Exception
            Throw New Exception("导出日志失败", ex)
        End Try
    End Sub

    Public Sub SaveServiceConfig(port As Integer)
        Try
            Dim folder = EnsureAppDataFolder()
            Dim filePath = Path.Combine(folder, "serviceconfig.xml")

            Dim doc = <?xml version="1.0" encoding="utf-8" standalone="yes"?>
                      <ServiceConfig>
                          <Port><%= port %></Port>
                          <LastSaved><%= DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") %></LastSaved>
                      </ServiceConfig>

            doc.Save(filePath)
        Catch ex As Exception
            Throw New Exception("保存服务配置失败", ex)
        End Try
    End Sub

    Public Function LoadServiceConfig() As Integer
        Try
            Dim folder = EnsureAppDataFolder()
            Dim filePath = Path.Combine(folder, "serviceconfig.xml")

            If Not File.Exists(filePath) Then
                Return 38080 ' 默认端口
            End If

            Dim doc = XDocument.Load(filePath)

            If Integer.TryParse(doc.Root.<Port>.Value, Nothing) Then
                Return Integer.Parse(doc.Root.<Port>.Value)
            End If

            Return 38080 ' 默认端口
        Catch ex As Exception
            Return 38080 ' 默认端口
        End Try
    End Function
End Module