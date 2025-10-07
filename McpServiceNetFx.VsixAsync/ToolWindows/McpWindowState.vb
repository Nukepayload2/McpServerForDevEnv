Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports Microsoft.VisualStudio.Shell
Imports Microsoft.VisualStudio.Shell.Interop

Namespace ToolWindows
    ''' <summary>
    ''' MCP 工具授权和服务状态信息
    ''' </summary>
    Public Class McpToolState
        Public Property ToolName As String
        Public Property IsAuthorized As Boolean
        Public Property IsEnabled As Boolean
        Public Property Description As String
        Public Property LastUsed As DateTime?
        Public Property UsageCount As Integer
    End Class

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
    ''' MCP 窗口状态管理类
    ''' </summary>
    Public Class McpWindowState
        Public Property Tools As New List(Of McpToolState)
        Public Property Services As New List(Of McpServiceState)

        Private ReadOnly _activityLog As IVsActivityLog

        Public Sub New()
            ' 获取 ActivityLog 服务
            _activityLog = CType(Package.GetGlobalService(GetType(SVsActivityLog)), IVsActivityLog)

            InitializeTools()
            InitializeServices()
        End Sub

        Private Sub InitializeTools()
            ' 初始化MCP工具，参考 McpServiceNetFx 的功能
            Tools.AddRange(New McpToolState() {
                New McpToolState With {
                    .ToolName = "文件系统操作",
                    .IsAuthorized = True,
                    .IsEnabled = True,
                    .Description = "读取、写入和管理文件系统",
                    .LastUsed = DateTime.Now.AddMinutes(-5),
                    .UsageCount = 42
                },
                New McpToolState With {
                    .ToolName = "代码执行",
                    .IsAuthorized = False,
                    .IsEnabled = False,
                    .Description = "执行代码和脚本",
                    .LastUsed = Nothing,
                    .UsageCount = 0
                },
                New McpToolState With {
                    .ToolName = "数据库访问",
                    .IsAuthorized = True,
                    .IsEnabled = True,
                    .Description = "连接和操作数据库",
                    .LastUsed = DateTime.Now.AddHours(-1),
                    .UsageCount = 15
                },
                New McpToolState With {
                    .ToolName = "网络请求",
                    .IsAuthorized = True,
                    .IsEnabled = False,
                    .Description = "发送HTTP请求和API调用",
                    .LastUsed = DateTime.Now.AddDays(-1),
                    .UsageCount = 8
                }
            })
        End Sub

        Private Sub InitializeServices()
            ' 初始化单个MCP服务
            Services.Add(New McpServiceState With {
                .IsRunning = False,
                .Port = 3000,
                .Status = "已停止",
                .StartTime = Nothing
            })
        End Sub

        ''' <summary>
        ''' 记录服务操作日志
        ''' </summary>
        ''' <param name="action">执行的操作</param>
        ''' <param name="result">操作结果</param>
        ''' <param name="details">详细信息</param>
        Public Sub LogServiceAction(action As String, result As String, details As String)
            Try
                If _activityLog IsNot Nothing Then
                    Dim message = $"Service Action: {action}, Result: {result}, Details: {details}"
                    _activityLog.LogEntry(CUInt(__ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION), "ServiceAction", message)
                End If
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine($"MCP ActivityLog Error: {ex.Message}")
            End Try
        End Sub

        ''' <summary>
        ''' 记录工具操作日志
        ''' </summary>
        ''' <param name="operation">操作名称</param>
        ''' <param name="result">操作结果</param>
        ''' <param name="details">详细信息</param>
        Public Sub LogToolOperation(operation As String, result As String, details As String)
            Try
                If _activityLog IsNot Nothing Then
                    Dim message = $"Tool Operation: {operation}, Result: {result}, Details: {details}"
                    _activityLog.LogEntry(CUInt(__ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION), "ToolOperation", message)
                End If
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
                If _activityLog IsNot Nothing Then
                    _activityLog.LogEntry(CUInt(__ACTIVITYLOG_ENTRYTYPE.ALE_ERROR), category, message)
                End If
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
                If _activityLog IsNot Nothing Then
                    _activityLog.LogEntry(CUInt(__ACTIVITYLOG_ENTRYTYPE.ALE_WARNING), category, message)
                End If
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
                If _activityLog IsNot Nothing Then
                    _activityLog.LogEntry(CUInt(__ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION), category, message)
                End If
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine($"MCP ActivityLog Error: {ex.Message}")
            End Try
        End Sub

        ''' <summary>
        ''' 授权所有工具
        ''' </summary>
        Public Sub AuthorizeAllTools()
            For Each tool In Tools
                tool.IsAuthorized = True
                LogToolOperation("AuthorizeTool", "Success", $"工具 {tool.ToolName} 已授权")
            Next
        End Sub

        ''' <summary>
        ''' 撤销所有工具授权
        ''' </summary>
        Public Sub RevokeAllToolAuthorization()
            For Each tool In Tools
                tool.IsAuthorized = False
                tool.IsEnabled = False
                LogToolOperation("RevokeAuthorization", "Success", $"工具 {tool.ToolName} 授权已撤销")
            Next
        End Sub

        ''' <summary>
        ''' 启动 MCP 服务
        ''' </summary>
        Public Sub StartService()
            If Services.Count > 0 Then
                Dim service = Services(0)
                If service.HasPermission Then
                    service.IsRunning = True
                    service.Status = "运行中"
                    service.StartTime = DateTime.Now
                    LogServiceAction("StartService", "Success", "MCP 服务已启动")
                Else
                    LogWarning("ServicePermission", "MCP 服务缺少权限，无法启动")
                End If
            End If
        End Sub

        ''' <summary>
        ''' 停止 MCP 服务
        ''' </summary>
        Public Sub StopService()
            If Services.Count > 0 Then
                Dim service = Services(0)
                service.IsRunning = False
                service.Status = "已停止"
                service.StartTime = Nothing
                LogServiceAction("StopService", "Success", "MCP 服务已停止")
            End If
        End Sub

        ''' <summary>
        ''' 获取 MCP JSON 配置示例
        ''' </summary>
        Public Function GetMcpJsonConfig() As String
            Return $"{{
  ""mcpServers"": {{
    ""mcp-service-netfx"": {{
      ""command"": ""dotnet"",
      ""args"": [""run"", ""--project"", ""{Environment.CurrentDirectory}\McpServiceNetFx.dll""],
      ""env"": {{
        ""DOTNET_ENVIRONMENT"": ""Development""
      }}
    }}
  }}
}}"
        End Function

        ''' <summary>
        ''' 获取 Claude CLI 配置示例
        ''' </summary>
        Public Function GetClaudeCliConfig() As String
            Return $"# Claude CLI 配置示例
# 创建或编辑 ~/.claude/config.json

{{
  ""mcpServers"": {{
    ""mcp-service-netfx"": {{
      ""command"": ""dotnet"",
      ""args"": [""run"", ""--project"", ""{Environment.CurrentDirectory}\McpServiceNetFx.dll""],
      ""env"": {{
        ""DOTNET_ENVIRONMENT"": ""Development""
      }}
    }}
  }}
}}

# 或者使用 .claude_desktop_config.json 文件
# 路径: %APPDATA%\Claude\.claude_desktop_config.json (Windows)

# 然后使用以下命令启动 Claude:
# claude --mcp mcp-service-netfx
"
        End Function

        ''' <summary>
        ''' 检查工具权限
        ''' </summary>
        Public Function CheckToolPermission(toolName As String) As Boolean
            Dim tool = Tools.FirstOrDefault(Function(t) t.ToolName = toolName)
            Return tool?.IsAuthorized AndAlso tool?.IsEnabled
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
            Dim authorizedCount As Integer = 0
            Dim enabledCount As Integer = 0
            For Each tool In Tools
                If tool.IsAuthorized Then authorizedCount += 1
                If tool.IsEnabled Then enabledCount += 1
            Next
            Return $"工具: {Tools.Count} (授权: {authorizedCount}, 启用: {enabledCount})"
        End Function
    End Class
End Namespace