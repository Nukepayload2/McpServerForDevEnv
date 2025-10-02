Imports System.IO
Imports System.Reflection

Public Module PersistenceModule
    Private ReadOnly LocalAppDataPath As String = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
    Private ReadOnly AppDataFolder As String = Path.Combine(LocalAppDataPath, Assembly.GetExecutingAssembly().GetName().Name)

    Public Enum PermissionLevel
        Allow
        Ask
        Deny
    End Enum

    Public Structure LogEntry
        Public Timestamp As DateTime
        Public Operation As String
        Public Result As String
        Public Details As String
    End Structure

    Public Structure FeaturePermission
        Public FeatureName As String
        Public Description As String
        Public Permission As PermissionLevel
    End Structure

    Private Function EnsureAppDataFolder() As String
        If Not Directory.Exists(AppDataFolder) Then
            Directory.CreateDirectory(AppDataFolder)
        End If
        Return AppDataFolder
    End Function

    Public Sub SavePermissions(permissions As IEnumerable(Of FeaturePermission))
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

    Public Function LoadPermissions() As List(Of FeaturePermission)
        Try
            Dim folder = EnsureAppDataFolder()
            Dim filePath = Path.Combine(folder, "permissions.xml")

            If Not File.Exists(filePath) Then
                Return GetDefaultPermissions()
            End If

            Dim doc = XDocument.Load(filePath)
            Dim permissions = New List(Of FeaturePermission)()

            For Each element In doc.Root.Elements("Permission")
                Dim permission As New FeaturePermission With {
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
            ' 如果加载失败，返回默认权限
            Return GetDefaultPermissions()
        End Try
    End Function

    Private Function GetDefaultPermissions() As List(Of FeaturePermission)
        Return New List(Of FeaturePermission) From {
            New FeaturePermission With {
                .FeatureName = "build_solution",
                .Description = "构建整个解决方案",
                .Permission = PermissionLevel.Ask
            },
            New FeaturePermission With {
                .FeatureName = "build_project",
                .Description = "构建指定项目",
                .Permission = PermissionLevel.Ask
            },
            New FeaturePermission With {
                .FeatureName = "get_error_list",
                .Description = "获取当前的错误和警告列表",
                .Permission = PermissionLevel.Allow
            },
            New FeaturePermission With {
                .FeatureName = "get_solution_info",
                .Description = "获取当前解决方案信息",
                .Permission = PermissionLevel.Allow
            }
        }
    End Function

    Public Sub SaveLogs(logs As IEnumerable(Of LogEntry))
        Try
            Dim folder = EnsureAppDataFolder()
            Dim filePath = Path.Combine(folder, "logs.xml")

            Dim doc = <?xml version="1.0" encoding="utf-8" standalone="yes"?>
                      <Logs>
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

    Public Function LoadLogs() As List(Of LogEntry)
        Try
            Dim folder = EnsureAppDataFolder()
            Dim filePath = Path.Combine(folder, "logs.xml")

            If Not File.Exists(filePath) Then
                Return New List(Of LogEntry)()
            End If

            Dim doc = XDocument.Load(filePath)
            Dim logs = New List(Of LogEntry)()

            For Each element In doc.Root.Elements("Log")
                Dim log As New LogEntry With {
                    .Timestamp = DateTime.Parse(element.@Timestamp),
                    .Operation = element.@Operation,
                    .Result = element.@Result,
                    .Details = element.@Details
                }
                logs.Add(log)
            Next

            Return logs
        Catch ex As Exception
            Return New List(Of LogEntry)()
        End Try
    End Function

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
                Return 8080 ' 默认端口
            End If

            Dim doc = XDocument.Load(filePath)

            If Integer.TryParse(doc.Root.<Port>.Value, Nothing) Then
                Return Integer.Parse(doc.Root.<Port>.Value)
            End If

            Return 8080 ' 默认端口
        Catch ex As Exception
            Return 8080 ' 默认端口
        End Try
    End Function
End Module