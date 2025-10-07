Imports System
Imports System.Collections.ObjectModel
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.IO
Imports System.Linq
Imports Microsoft.VisualStudio.Shell
Imports Microsoft.VisualStudio.Shell.Interop
Imports EnvDTE80
Imports System.Threading.Tasks
Imports System.Runtime.CompilerServices
Imports McpServiceNetFx.Models
Imports McpServiceNetFx.VsixAsync.Helpers

Namespace ToolWindows
  
    ''' <summary>
    ''' MCP 服务状态
    ''' </summary>
    Public Class McpServiceState
        Public Property IsRunning As Boolean
        Public Property Port As Integer
        Public Property Status As String
        Public Property StartTime As DateTime?
        Public Property HasPermission As Boolean = True
    End Class

    ''' <summary>
    ''' ActivityLog 日志项
    ''' </summary>
    Public Class ActivityLogItem
        Public Property Timestamp As String
        Public Property Level As String
        Public Property Category As String
        Public Property Message As String

        Public Sub New(level As String, category As String, message As String)
            Me.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            Me.Level = level
            Me.Category = category
            Me.Message = message
        End Sub
    End Class

    ''' <summary>
    ''' MCP 窗口状态管理类
    ''' </summary>
    Public Class McpWindowState
        Implements IMcpLogger, IMcpPermissionHandler

        Public Property Tools As New List(Of PermissionItem)
        Public Property Services As New List(Of McpServiceState)
        Public Property LogItems As New ObservableCollection(Of ActivityLogItem)

        Private ReadOnly _activityLog As IVsActivityLog
        Private ReadOnly _dte2 As DTE2
        Private _mcpService As McpService
        Private _toolManager As VisualStudioToolManager
        Private _permissionItems As New List(Of PermissionItem)()
        Private ReadOnly _settingsHelper As SettingsPersistenceHelper

        Public Sub New(package As AsyncPackage)
            ' 初始化设置持久化辅助类
            _settingsHelper = New SettingsPersistenceHelper(package)

            ' 获取 ActivityLog 服务
            Try
                _activityLog = package.GetService(Of SVsActivityLog, IVsActivityLog)()
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine($"无法获取 ActivityLog 服务: {ex.Message}")
            End Try

            ' 获取 DTE2 服务（简化版本）
            Try
                _dte2 = package.GetService(Of SDTE, DTE2)()
            Catch ex As Exception
                LogError("ServiceError", $"无法获取 DTE2 服务: {ex.Message}")
            End Try

            ' 创建工具管理器（简化版本）
            Try
                _toolManager = New VisualStudioToolManager(Me, Me)
            Catch ex As Exception
                LogError("ServiceError", $"无法创建工具管理器: {ex.Message}")
            End Try

            InitializeTools()
            InitializeServices()
        End Sub

        Private Sub InitializeTools()
            ' 参考 MainWindow.Permissions.vb 的 LoadPermissions 方法
            LoadPermissions()
        End Sub

        ''' <summary>
        ''' 加载权限配置 - 参考 MainWindow.Permissions.vb
        ''' </summary>
        Private Sub LoadPermissions()
            Try
                ' 首先尝试从Visual Studio设置加载保存的权限配置
                Dim savedPermissionDict = _settingsHelper.LoadPermissions()

                If savedPermissionDict.Any() Then
                    ' 从工具管理器获取默认权限作为基础
                    Dim defaultPermissions = GetDefaultPermissionsFromToolManager()

                    ' 应用保存的权限级别
                    Tools.Clear()
                    For Each defaultPermission In defaultPermissions
                        Dim savedPermission As PermissionLevel = Nothing
                        Dim permissionValue As PermissionLevel
                        If savedPermissionDict.TryGetValue(defaultPermission.FeatureName, savedPermission) Then
                            permissionValue = savedPermission
                        Else
                            permissionValue = defaultPermission.Permission
                        End If

                        Dim permission = New PermissionItem With {
                            .FeatureName = defaultPermission.FeatureName,
                            .Description = defaultPermission.Description,
                            .Permission = permissionValue
                        }
                        Tools.Add(permission)
                    Next

                    ' 添加保存的权限中存在但默认权限中没有的工具
                    For Each kvp In savedPermissionDict
                        Dim featureName = kvp.Key
                        Dim savedPermission = kvp.Value

                        If Not Tools.Any(Function(t) t.FeatureName = featureName) Then
                            Tools.Add(New PermissionItem With {
                                .FeatureName = featureName,
                                .Description = featureName, ' 使用功能名作为描述
                                .Permission = savedPermission
                            })
                        End If
                    Next

                    LogServiceAction("权限加载", "成功", $"从Visual Studio设置加载了 {Tools.Count} 个工具权限")
                Else
                    ' 如果没有保存的配置，从工具管理器加载默认权限
                    Dim defaultPermissions = GetDefaultPermissionsFromToolManager()
                    If defaultPermissions.Any() Then
                        Tools.Clear()
                        Tools.AddRange(defaultPermissions)
                        LogServiceAction("权限加载", "成功", $"从工具管理器加载了 {Tools.Count} 个工具权限")
                    Else
                        AddDefaultTools()
                    End If
                End If
            Catch ex As Exception
                LogError("权限错误", $"加载权限配置失败: {ex.Message}")
                AddDefaultTools()
            End Try
        End Sub

        ''' <summary>
        ''' 从工具管理器获取默认权限配置
        ''' </summary>
        Private Function GetDefaultPermissionsFromToolManager() As List(Of PermissionItem)
            If _toolManager IsNot Nothing AndAlso _toolManager.IsInitialized Then
                Try
                    Return _toolManager.GetDefaultPermissions()
                Catch ex As Exception
                    LogError("工具管理器错误", $"获取默认权限失败: {ex.Message}")
                End Try
            End If
            Return New List(Of PermissionItem)()
        End Function

        ''' <summary>
        ''' 添加默认工具权限配置
        ''' </summary>
        Private Sub AddDefaultTools()
            Tools.Clear()
            Tools.AddRange(New PermissionItem() {
                New PermissionItem With {
                    .FeatureName = "解决方案信息",
                    .Description = "获取当前解决方案的信息",
                    .Permission = PermissionLevel.Allow
                },
                New PermissionItem With {
                    .FeatureName = "项目构建",
                    .Description = "构建当前项目或解决方案",
                    .Permission = PermissionLevel.Allow
                },
                New PermissionItem With {
                    .FeatureName = "错误列表",
                    .Description = "获取当前错误列表",
                    .Permission = PermissionLevel.Allow
                },
                New PermissionItem With {
                    .FeatureName = "文档操作",
                    .Description = "读取和编辑当前文档",
                    .Permission = PermissionLevel.Ask
                }
            })
            LogServiceAction("权限加载", "部分成功", $"添加了 {Tools.Count} 个默认工具权限")
        End Sub

        Private Sub InitializeServices()
            ' 初始化单个MCP服务
            Services.Add(New McpServiceState With {
                .IsRunning = False,
                .Port = 38080,
                .Status = "已停止",
                .StartTime = Nothing
            })
        End Sub


        ''' <summary>
        ''' 记录工具操作日志
        ''' </summary>
        ''' <param name="operation">操作名称</param>
        ''' <param name="result">操作结果</param>
        ''' <param name="details">详细信息</param>
        Public Sub LogToolOperation(operation As String, result As String, details As String)
            Try
                Dim message = $"Tool Operation: {operation}, Result: {result}, Details: {details}"

                ' 记录到 ActivityLog
                If _activityLog IsNot Nothing Then
                    _activityLog.LogEntry(CUInt(__ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION), "ToolOperation", message)
                End If

                ' 添加到界面日志列表 - 参考 MainWindow.Logging.vb，不限制数量
                LogItems.Insert(0, New ActivityLogItem("INFO", "ToolOperation", message))
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine($"MCP ActivityLog Error: {ex.Message}")
            End Try
        End Sub

        ''' <summary>
        ''' 记录错误日志
        ''' </summary>
        ''' <param name="category">日志分类</param>
        ''' <param name="message">日志消息</param>
        Public Sub LogError(category As String, message As String)
            Try
                ' 记录到 ActivityLog
                If _activityLog IsNot Nothing Then
                    _activityLog.LogEntry(CUInt(__ACTIVITYLOG_ENTRYTYPE.ALE_ERROR), category, message)
                End If

                ' 添加到界面日志列表 - 参考 MainWindow.Logging.vb，不限制数量
                LogItems.Insert(0, New ActivityLogItem("ERROR", category, message))
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine($"MCP ActivityLog Error: {ex.Message}")
            End Try
        End Sub

        ''' <summary>
        ''' 记录警告日志
        ''' </summary>
        ''' <param name="category">日志分类</param>
        ''' <param name="message">日志消息</param>
        Public Sub LogWarning(category As String, message As String)
            Try
                ' 记录到 ActivityLog
                If _activityLog IsNot Nothing Then
                    _activityLog.LogEntry(CUInt(__ACTIVITYLOG_ENTRYTYPE.ALE_WARNING), category, message)
                End If

                ' 添加到界面日志列表 - 参考 MainWindow.Logging.vb，不限制数量
                LogItems.Insert(0, New ActivityLogItem("WARN", category, message))
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine($"MCP ActivityLog Error: {ex.Message}")
            End Try
        End Sub

        ''' <summary>
        ''' 记录信息日志
        ''' </summary>
        ''' <param name="category">日志分类</param>
        ''' <param name="message">日志消息</param>
        Public Sub LogInfo(category As String, message As String)
            Try
                ' 记录到 ActivityLog
                If _activityLog IsNot Nothing Then
                    _activityLog.LogEntry(CUInt(__ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION), category, message)
                End If

                ' 添加到界面日志列表 - 参考 MainWindow.Logging.vb，不限制数量
                LogItems.Insert(0, New ActivityLogItem("INFO", category, message))
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine($"MCP ActivityLog Error: {ex.Message}")
            End Try
        End Sub

        ''' <summary>
        ''' 设置所有权限 - 参考 MainWindow.Permissions.vb 的 SetAllPermissions 方法
        ''' </summary>
        Public Sub SetAllPermissions(permission As PermissionLevel)
            For Each tool In Tools
                tool.Permission = permission
            Next
            LogToolOperation("SetAllPermissions", "Success", $"批量设置所有工具权限为: {permission}")
        End Sub

        ''' <summary>
        ''' 保存权限配置
        ''' </summary>
        Public Sub SavePermissions()
            Try
                ' 使用Visual Studio设置保存权限配置
                _settingsHelper.SavePermissions(Tools)
                LogServiceAction("权限保存", "成功", $"保存了 {Tools.Count} 个权限配置")
            Catch ex As Exception
                LogError("权限保存", $"保存权限配置失败: {ex.Message}")
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' 重新加载权限配置
        ''' </summary>
        Public Sub ReloadPermissions()
            LoadPermissions()
            LogServiceAction("权限重新加载", "成功", $"重新加载了 {Tools.Count} 个工具权限")
        End Sub

        ''' <summary>
        ''' 启动 MCP 服务
        ''' </summary>
        Public Sub StartService()
            Try
                If Services.Count > 0 Then
                    Dim service = Services(0)

                    If _dte2 Is Nothing Then
                        LogError("ServiceError", "无法获取 DTE2 服务")
                        Return
                    End If

                    If _mcpService IsNot Nothing AndAlso _mcpService.IsRunning Then
                        LogServiceAction("StartService", "Failed", "MCP 服务已在运行中")
                        Return
                    End If

                    ' 创建并启动真实的 MCP 服务
                    _mcpService = New McpService(_dte2, service.Port, Me, New DispatcherService(), _toolManager, New ClipboardService(), New InteractionService())
                    _mcpService.Start()

                    ' 更新服务状态
                    service.IsRunning = True
                    service.Status = "运行中"
                    service.StartTime = DateTime.Now

                    LogServiceAction("StartService", "Success", $"MCP 服务已启动，端口: {service.Port}")
                End If
            Catch ex As Exception
                LogError("ServiceError", $"启动 MCP 服务失败: {ex.Message}")
                If Services.Count > 0 Then
                    Services(0).Status = "启动失败"
                End If
            End Try
        End Sub

        ''' <summary>
        ''' 停止 MCP 服务
        ''' </summary>
        Public Sub StopService()
            Try
                If _mcpService IsNot Nothing Then
                    _mcpService.Stop()
                    _mcpService.Dispose()
                    _mcpService = Nothing
                End If

                If Services.Count > 0 Then
                    Dim service = Services(0)
                    service.IsRunning = False
                    service.Status = "已停止"
                    service.StartTime = Nothing
                End If

                LogServiceAction("StopService", "Success", "MCP 服务已停止")
            Catch ex As Exception
                LogError("ServiceError", $"停止 MCP 服务失败: {ex.Message}")
            End Try
        End Sub

        ''' <summary>
        ''' 获取 MCP JSON 配置示例
        ''' </summary>
        Public Function GetMcpJsonConfig() As String
            Try
                If Services.Count > 0 Then
                    Dim service = Services(0)
                    Dim serverName = "@nukepayload2/devenv.wrapper"
                    Dim port = service.Port

                    ' 生成 JSON 配置
                    Return $"{{
  ""mcpServers"": {{
    ""{serverName}"": {{
      ""type"": ""http"",
      ""url"": ""http://localhost:{port}/mcp/""
    }}
  }}""
}}"
                Else
                    Return "// 服务未启动，无法生成配置"
                End If
            Catch ex As Exception
                Return $"// 生成配置失败: {ex.Message}"
            End Try
        End Function

        ''' <summary>
        ''' 获取 Claude CLI 配置示例
        ''' </summary>
        Public Function GetClaudeCliConfig() As String
            Try
                If Services.Count > 0 Then
                    Dim service = Services(0)
                    Dim port = service.Port

                    ' 生成 Claude CLI 配置
                    Return $"claude mcp add --transport http devenv ""http://localhost:{port}/mcp/"""
                Else
                    Return "# 服务未启动，无法生成配置"
                End If
            Catch ex As Exception
                Return $"# 生成配置失败: {ex.Message}"
            End Try
        End Function

        ''' <summary>
        ''' 检查工具权限
        ''' </summary>
        Public Function CheckToolPermission(toolName As String) As Boolean
            Dim tool = Tools.FirstOrDefault(Function(t) t.FeatureName = toolName)
            Return tool?.Permission <> PermissionLevel.Deny
        End Function

        ''' <summary>
        ''' 获取服务状态摘要
        ''' </summary>
        Public Function GetServiceSummary() As String
            Dim runningCount As Integer = 0
            For Each service In Services
                If service.IsRunning Then runningCount += 1
            Next
            Return $"服务: {Services.Count} (运行中: {runningCount})"
        End Function

        ''' <summary>
        ''' 获取工具状态摘要
        ''' </summary>
        Public Function GetToolSummary() As String
            Dim allowedCount As Integer = 0
            Dim askCount As Integer = 0
            Dim deniedCount As Integer = 0
            For Each tool In Tools
                Select Case tool.Permission
                    Case PermissionLevel.Allow
                        allowedCount += 1
                    Case PermissionLevel.Ask
                        askCount += 1
                    Case PermissionLevel.Deny
                        deniedCount += 1
                End Select
            Next
            Return $"工具: {Tools.Count} (允许: {allowedCount}, 询问: {askCount}, 拒绝: {deniedCount})"
        End Function


        ''' <summary>
        ''' 实现 IMcpLogger.LogServiceAction
        ''' </summary>
        Public Sub LogServiceAction(action As String, result As String, details As String) Implements IMcpLogger.LogServiceAction
            Dim message = $"Service Action: {action}, Result: {result}, Details: {details}"
            LogEntry(__ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION, "ServiceAction", message)
        End Sub

        ''' <summary>
        ''' 实现 IMcpLogger.LogMcpRequest
        ''' </summary>
        Public Sub LogMcpRequest(operation As String, result As String, details As String) Implements IMcpLogger.LogMcpRequest
            Dim message = $"MCP Request: {operation}, Result: {result}, Details: {details}"
            LogEntry(__ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION, "McpRequest", message)
        End Sub

        ''' <summary>
        ''' 实现 IMcpPermissionHandler.CheckPermission
        ''' </summary>
        Public Function CheckPermission(featureName As String, operationDescription As String) As Boolean Implements IMcpPermissionHandler.CheckPermission
            Dim permissionItem = _permissionItems.FirstOrDefault(Function(p) p.FeatureName = featureName)
            If permissionItem Is Nothing Then
                LogOperation("权限检查", "未找到", $"功能 {featureName} 的权限配置未找到，默认允许")
                Return True ' 默认允许
            End If

            Select Case permissionItem.Permission
                Case PermissionLevel.Allow
                    LogOperation(featureName, "允许", operationDescription)
                    Return True
                Case PermissionLevel.Deny
                    LogOperation(featureName, "拒绝", operationDescription)
                    Return False
                Case PermissionLevel.Ask
                    LogOperation(featureName, "询问", operationDescription)
                    ' 在 VSIX 环境中，默认询问时允许
                    LogInfo("权限询问", $"功能 {featureName} 请求执行 {operationDescription}，默认允许")
                    Return True
                Case Else
                    LogOperation(featureName, "未知权限", $"权限级别: {permissionItem.Permission}")
                    Return False
            End Select
        End Function

        ''' <summary>
        ''' 获取工具权限
        ''' </summary>
        Public Function GetToolPermission(toolName As String) As PermissionLevel
            Dim permissionItem = _permissionItems.FirstOrDefault(Function(p) p.FeatureName = toolName)
            If permissionItem Is Nothing Then
                Return PermissionLevel.Ask ' 默认询问
            End If
            Return permissionItem.Permission
        End Function

        ''' <summary>
        ''' 设置工具权限
        ''' </summary>
        Public Sub SetToolPermission(toolName As String, permissionLevel As PermissionLevel)
            Dim permissionItem = _permissionItems.FirstOrDefault(Function(p) p.FeatureName = toolName)
            If permissionItem IsNot Nothing Then
                permissionItem.Permission = permissionLevel
                LogOperation("权限设置", "成功", $"工具 {toolName} 权限设置为 {permissionLevel}")
            Else
                ' 添加新的权限项
                _permissionItems.Add(New PermissionItem With {
                    .FeatureName = toolName,
                    .Description = $"工具 {toolName}",
                    .Permission = permissionLevel
                })
                LogOperation("权限设置", "成功", $"为工具 {toolName} 添加权限配置: {permissionLevel}")
            End If
        End Sub

        ''' <summary>
        ''' 清空日志
        ''' </summary>
        Public Sub ClearLogItems()
            LogItems.Clear()
            LogInfo("UIAction", "界面日志已清空")
        End Sub

        ''' <summary>
        ''' 记录操作日志（通用方法）
        ''' </summary>
        Private Sub LogOperation(category As String, result As String, message As String)
            LogInfo(category, $"{result} - {message}")
        End Sub

        ''' <summary>
        ''' 日志记录核心方法
        ''' </summary>
        Private Sub LogEntry(entryType As __ACTIVITYLOG_ENTRYTYPE, category As String, message As String)
            Try
                ' 记录到 ActivityLog
                If _activityLog IsNot Nothing Then
                    _activityLog.LogEntry(CUInt(entryType), category, message)
                End If

                ' 根据日志级别添加到界面日志列表 - 参考 MainWindow.Logging.vb，不限制数量
                Dim level As String
                Select Case entryType
                    Case __ACTIVITYLOG_ENTRYTYPE.ALE_ERROR
                        level = "ERROR"
                    Case __ACTIVITYLOG_ENTRYTYPE.ALE_WARNING
                        level = "WARN"
                    Case __ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION
                        level = "INFO"
                    Case Else
                        level = "INFO"
                End Select

                LogItems.Insert(0, New ActivityLogItem(level, category, message))
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine($"MCP ActivityLog Error: {ex.Message}")
            End Try
        End Sub
    End Class

    ''' <summary>
    ''' 调度器服务 - 简化实现
    ''' </summary>
    Public Class DispatcherService
        Implements IDispatcher

        Public Sub Invoke(job As Action) Implements IDispatcher.Invoke
            ' 在 VSIX 环境中直接同步执行
            job()
        End Sub

        Public Async Function InvokeAsync(job As Func(Of Task)) As Task Implements IDispatcher.InvokeAsync
            Await job()
        End Function

        Public Async Function InvokeAsync(job As Action) As Task Implements IDispatcher.InvokeAsync
            Await System.Threading.Tasks.Task.Run(job)
        End Function
    End Class

    ''' <summary>
    ''' 剪贴板服务
    ''' </summary>
    Public Class ClipboardService
        Implements IClipboard

        Public Sub SetText(text As String) Implements IClipboard.SetText
            Try
                System.Windows.Clipboard.SetText(text)
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine($"Clipboard error: {ex.Message}")
            End Try
        End Sub
    End Class

    ''' <summary>
    ''' 交互服务 - 简化实现
    ''' </summary>
    Public Class InteractionService
        Implements IInteraction

        Public Sub ShowCopyCommandDialog(title As String, message As String, command As String) Implements IInteraction.ShowCopyCommandDialog
            Try
                ' 在 VSIX 环境中使用 ActivityLog 记录，不显示对话框
                System.Diagnostics.Debug.WriteLine($"{title}: {message}")
                System.Diagnostics.Debug.WriteLine($"Command: {command}")
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine($"Interaction error: {ex.Message}")
            End Try
        End Sub
    End Class
End Namespace