Imports System
Imports System.Collections.ObjectModel
Imports System.Collections.Generic
Imports System.Linq
Imports Microsoft.VisualStudio.Shell
Imports Microsoft.VisualStudio.Shell.Interop
Imports EnvDTE80
Imports System.Threading.Tasks
Imports McpServiceNetFx.VsixAsync.Helpers
Imports Microsoft.VisualStudio.Threading
Imports SR = McpServiceNetFx.My.Resources.Resources

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
        Public Property ServerConfiguration As New ServerConfiguration()

        Private ReadOnly _activityLog As IVsActivityLog
        Private ReadOnly _dte2 As DTE2
        Private _mcpService As McpService
        Private _toolManager As VisualStudioToolManager
        Private ReadOnly _settingsHelper As SettingsPersistenceHelper

        Private ReadOnly _joinableTaskFactory As JoinableTaskFactory

        Public Sub New(package As AsyncPackage)
            ' 初始化设置持久化辅助类
            _settingsHelper = New SettingsPersistenceHelper(package)

            ' 获取 ActivityLog 服务
            Try
                _activityLog = package.GetService(Of SVsActivityLog, IVsActivityLog)()
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine(String.Format(SR.LogCannotGetActivityLog, ex.Message))
            End Try

            ' 获取 DTE2 服务（简化版本）
            Try
                _dte2 = package.GetService(Of SDTE, DTE2)()
            Catch ex As Exception
                LogError(SR.LogCategoryServiceError, String.Format(SR.LogCannotGetDte2, ex.Message))
            End Try
            _joinableTaskFactory = package.JoinableTaskFactory
            ' 创建工具管理器（简化版本）
            Try
                _toolManager = New VisualStudioToolManager(Me, Me)

                ' 初始化工具管理器，传入 DTE2 和调度器
                If _dte2 IsNot Nothing Then
                    _toolManager.CreateVsTools(_dte2, New DispatcherService(_joinableTaskFactory))
                    LogInfo(SR.LogCategoryToolManager, SR.LogToolManagerInitialized)
                Else
                    LogError(SR.LogCategoryToolManager, SR.LogToolManagerInitFailed)
                End If
            Catch ex As Exception
                LogError(SR.LogCategoryServiceError, String.Format(SR.LogCannotCreateToolManager, ex.Message))
            End Try

            ' 加载服务器配置
            LoadServerConfiguration()

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

                    LogServiceAction(SR.LogCategoryPermissionLoad, SR.LogResultSuccess, String.Format(SR.LogPermissionsLoadedFromSettings, Tools.Count))
                Else
                    ' 如果没有保存的配置，从工具管理器加载默认权限
                    Dim defaultPermissions = GetDefaultPermissionsFromToolManager()
                    If defaultPermissions.Any() Then
                        Tools.Clear()
                        Tools.AddRange(defaultPermissions)
                        LogServiceAction(SR.LogCategoryPermissionLoad, SR.LogResultSuccess, String.Format(SR.LogPermissionsLoadedFromToolManager, Tools.Count))
                    Else
                        AddDefaultTools()
                    End If
                End If
            Catch ex As Exception
                LogError(SR.LogCategoryPermissionError, String.Format(SR.LogLoadPermissionsFailed, ex.Message))
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
                    LogError(SR.LogCategoryToolManagerError, String.Format(SR.LogGetDefaultPermissionsFailed, ex.Message))
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
                    .FeatureName = SR.DefaultToolSolutionInfo,
                    .Description = SR.DefaultToolSolutionInfoDesc,
                    .Permission = PermissionLevel.Allow
                },
                New PermissionItem With {
                    .FeatureName = SR.DefaultToolProjectBuild,
                    .Description = SR.DefaultToolProjectBuildDesc,
                    .Permission = PermissionLevel.Allow
                },
                New PermissionItem With {
                    .FeatureName = SR.DefaultToolErrorList,
                    .Description = SR.DefaultToolErrorListDesc,
                    .Permission = PermissionLevel.Allow
                },
                New PermissionItem With {
                    .FeatureName = SR.DefaultToolDocumentOps,
                    .Description = SR.DefaultToolDocumentOpsDesc,
                    .Permission = PermissionLevel.Ask
                }
            })
            LogServiceAction(SR.LogCategoryPermissionLoad, SR.LogResultPartialSuccess, String.Format(SR.LogDefaultToolsAdded, Tools.Count))
        End Sub

        Private Sub InitializeServices()
            ' 初始化单个MCP服务，使用配置的端口
            Services.Add(New McpServiceState With {
                .IsRunning = False,
                .Port = ServerConfiguration.Port,
                .Status = SR.StatusStopped,
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
            LogToolOperation(SR.LogOperationSetAllPermissions, SR.LogResultSuccess, String.Format(SR.LogBatchSetPermissions, permission))
        End Sub

        ''' <summary>
        ''' 保存权限配置
        ''' </summary>
        Public Sub SavePermissions()
            Try
                ' 使用Visual Studio设置保存权限配置
                _settingsHelper.SavePermissions(Tools)
                LogServiceAction(SR.LogCategoryPermissionSave, SR.LogResultSuccess, String.Format(SR.LogPermissionsSaved, Tools.Count))
            Catch ex As Exception
                LogError(SR.LogCategoryPermissionSave, String.Format(SR.LogSavePermissionsFailed, ex.Message))
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' 重新加载权限配置
        ''' </summary>
        Public Sub ReloadPermissions()
            LoadPermissions()
            LogServiceAction(SR.LogCategoryPermissionLoad, SR.LogResultSuccess, String.Format(SR.LogPermissionsReloaded, Tools.Count))
        End Sub

        ''' <summary>
        ''' 加载服务器配置
        ''' </summary>
        Private Sub LoadServerConfiguration()
            Try
                ServerConfiguration = _settingsHelper.LoadServerConfiguration()
                LogServiceAction(SR.LogCategoryConfigLoad, SR.LogResultSuccess, String.Format(SR.LogServerConfigLoaded, ServerConfiguration.Port))
            Catch ex As Exception
                LogError(SR.LogCategoryConfigLoad, String.Format(SR.LogLoadServerConfigFailed, ex.Message))
                ServerConfiguration = New ServerConfiguration()
            End Try
        End Sub

        ''' <summary>
        ''' 保存服务器配置
        ''' </summary>
        Public Sub SaveServerConfiguration()
            Try
                _settingsHelper.SaveServerConfiguration(ServerConfiguration)
                LogServiceAction(SR.LogCategoryConfigSave, SR.LogResultSuccess, String.Format(SR.LogServerConfigSaved, ServerConfiguration.Port))
            Catch ex As Exception
                LogError(SR.LogCategoryConfigSave, String.Format(SR.LogSaveServerConfigFailed, ex.Message))
                Throw
            End Try
        End Sub

        ''' <summary>
        ''' 启动 MCP 服务
        ''' </summary>
        Public Async Function StartServiceAsync() As Task
            Try
                If Services.Count > 0 Then
                    Dim service = Services(0)

                    If _dte2 Is Nothing Then
                        LogError(SR.LogCategoryServiceError, SR.LogCannotGetDte2)
                        Return
                    End If

                    If _mcpService IsNot Nothing AndAlso _mcpService.IsRunning Then
                        LogServiceAction(SR.LogOperationStartService, SR.LogResultFailed, SR.LogServiceAlreadyRunning)
                        Return
                    End If

                    ' 确保服务使用当前配置的端口
                    service.Port = ServerConfiguration.Port

                    ' 创建并启动真实的 MCP 服务
                    _mcpService = New McpService(_dte2, service.Port, Me,
                                                 New DispatcherService(_joinableTaskFactory),
                                                 _toolManager, New ClipboardService(), New InteractionService())
                    Await _mcpService.StartAsync()

                    ' 更新服务状态
                    service.IsRunning = True
                    service.Status = SR.StatusRunning
                    service.StartTime = DateTime.Now

                    LogServiceAction(SR.LogOperationStartService, SR.LogResultSuccess, String.Format(SR.LogServiceStarted, service.Port))
                End If
            Catch ex As Exception
                LogError(SR.LogCategoryServiceError, String.Format(SR.LogStartServiceFailed, ex.Message))
                If Services.Count > 0 Then
                    Services(0).Status = SR.StatusStartFailed
                End If
            End Try
        End Function

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
                    service.Status = SR.StatusStopped
                    service.StartTime = Nothing
                End If

                LogServiceAction(SR.LogOperationStopService, SR.LogResultSuccess, SR.LogServiceStopped)
            Catch ex As Exception
                LogError(SR.LogCategoryServiceError, String.Format(SR.LogStopServiceFailed, ex.Message))
            End Try
        End Sub

        ''' <summary>
        ''' 获取 MCP JSON 配置示例
        ''' </summary>
        Public Function GetMcpJsonConfig() As String
            Try
                Dim serverName = "@nukepayload2/devenv.wrapper"
                Dim port = ServerConfiguration.Port

                ' 生成 JSON 配置
                Return $"{{
  ""mcpServers"": {{
    ""{serverName}"": {{
      ""type"": ""http"",
      ""url"": ""http://localhost:{port}/mcp/""
    }}
  }}"""
            Catch ex As Exception
                Return $"// {String.Format(SR.LogConfigGenerationFailed, ex.Message)}"
            End Try
        End Function

        ''' <summary>
        ''' 获取 Claude CLI 配置示例
        ''' </summary>
        Public Function GetClaudeCliConfig() As String
            Try
                Dim port = ServerConfiguration.Port

                ' 生成 Claude CLI 配置
                Return $"claude mcp add --transport http devenv ""http://localhost:{port}/mcp/"""
            Catch ex As Exception
                Return $"# {String.Format(SR.LogConfigGenerationFailed, ex.Message)}"
            End Try
        End Function

        ''' <summary>
        ''' 获取服务状态摘要
        ''' </summary>
        Public Function GetServiceSummary() As String
            Dim runningCount As Integer = 0
            For Each service In Services
                If service.IsRunning Then runningCount += 1
            Next
            Return String.Format(SR.ServiceSummaryFormat, Services.Count, runningCount)
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
            Return String.Format(SR.ToolSummaryFormat, Tools.Count, allowedCount, askCount, deniedCount)
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
            Dim permissionItem = Tools.FirstOrDefault(Function(p) p.FeatureName = featureName)
            Dim level = PermissionLevel.Ask
            If permissionItem IsNot Nothing Then
                level = permissionItem.Permission
            End If

            Select Case level
                Case PermissionLevel.Allow
                    LogOperation(featureName, SR.PermissionAllowed, operationDescription)
                    Return True
                Case PermissionLevel.Deny
                    LogOperation(featureName, SR.PermissionDenied, operationDescription)
                    Return False
                Case PermissionLevel.Ask
                    LogOperation(featureName, SR.PermissionAsk, operationDescription)

                    ' 使用 CustomMessageBox 询问用户是否允许
                    Try
                        Dim message = String.Format(SR.PermissionConfirmMessage, featureName, operationDescription, Environment.NewLine)
                        Dim result = CustomMessageBox.Show(
                            Nothing,
                            message,
                            SR.PermissionConfirmTitle,
                            CustomMessageBox.MessageBoxType.Question,
                            True,
                            True
                        )

                        Select Case result
                            Case CustomMessageBox.MessageBoxResult.Yes
                                LogOperation(featureName, SR.PermissionUserAllowed, operationDescription)
                                Return True
                            Case CustomMessageBox.MessageBoxResult.No, CustomMessageBox.MessageBoxResult.None
                                LogOperation(featureName, SR.PermissionUserDenied, operationDescription)
                                Return False
                        End Select
                    Catch ex As Exception
                        LogError(SR.LogCategoryPermission, String.Format(SR.LogPermissionDialogFailed, ex.Message))
                        Return False
                    End Try
            End Select
            Return False
        End Function

        ''' <summary>
        ''' 清空日志
        ''' </summary>
        Public Sub ClearLogItems()
            LogItems.Clear()
            LogInfo(SR.LogCategoryUIAction, SR.LogUICleared)
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

        Private ReadOnly _joinableTaskFactory As JoinableTaskFactory

        Sub New(joinableTaskFactory As JoinableTaskFactory)
            _joinableTaskFactory = joinableTaskFactory
        End Sub

        Public Async Function InvokeAsync(job As Func(Of Task)) As Task Implements IDispatcher.InvokeAsync
            Await _joinableTaskFactory.SwitchToMainThreadAsync
            Await job()
        End Function

        Public Async Function InvokeAsync(job As Action) As Task Implements IDispatcher.InvokeAsync
            Await _joinableTaskFactory.SwitchToMainThreadAsync
            job()
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
    ''' 交互服务 - 使用新的 UI 组件实现
    ''' </summary>
    Public Class InteractionService
        Implements IInteraction

        Public Sub ShowCopyCommandDialog(title As String, message As String, command As String) Implements IInteraction.ShowCopyCommandDialog
            Try
                ' 尝试复制到剪贴板
                Dim clipboardSuccess = TryCopyToClipboard(command, SR.CommandDescription)

                ' 根据复制结果调整消息
                Dim displayMessage As String
                If clipboardSuccess Then
                    displayMessage = $"{message}{Environment.NewLine}{Environment.NewLine}{SR.CopyCommandSuccess}"
                Else
                    displayMessage = $"{message}{Environment.NewLine}{Environment.NewLine}{SR.CopyCommandFailed}"
                End If

                ' 显示用户消息窗口
                ShowUserMessageWindow(title, displayMessage, command)
            Catch ex As Exception
            End Try
        End Sub

        ''' <summary>
        ''' 尝试复制文本到剪贴板
        ''' </summary>
        Private Shared Function TryCopyToClipboard(text As String, Optional description As String = "") As Boolean
            Try
                System.Windows.Forms.Clipboard.SetText(text)
                Return True
            Catch ex As Exception
                Return False
            End Try
        End Function

        ''' <summary>
        ''' 显示用户消息窗口
        ''' </summary>
        Private Shared Sub ShowUserMessageWindow(title As String, message As String, command As String)
            Try
                Dim window As New UserMessageWindow(title, message, command) With {
                    .Owner = System.Windows.Application.Current.MainWindow,
                    .WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner
                }
                window.Show()
            Catch ex As Exception
            End Try
        End Sub
    End Class
End Namespace