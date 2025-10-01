Imports System
Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports Microsoft.VisualStudio.Imaging
Imports Microsoft.VisualStudio.Shell

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
    ''' MCP 工具窗口类
    ''' </summary>
    <Guid("c7b4a4f9-2d6e-4f8b-9e5a-6d7c8e9f0a1b")>
    Public Class McpToolWindow
        Inherits ToolWindowPane

        Public Const WindowGuidString As String = "c7b4a4f9-2d6e-4f8b-9e5a-6d7c8e9f0a1b"
        Public Const Title As String = "MCP 工具管理器"

        Private _state As McpWindowState

        Public Sub New(state As McpWindowState)
            MyBase.New()
            Caption = Title
            BitmapImageMoniker = KnownMonikers.Settings
            _state = state

            ' 创建WPF控件
            Content = New McpToolWindowControl(state)
        End Sub
    End Class

    ''' <summary>
    ''' MCP 窗口状态
    ''' </summary>
    Public Class McpWindowState
        Public Property Tools As New List(Of McpToolState)
        Public Property Services As New List(Of McpServiceState)

        Public Sub New()
            ' 初始化一些示例MCP工具
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

            ' 初始化MCP服务
            Services.AddRange(New McpServiceState() {
                New McpServiceState With {
                    .ServiceName = "本地MCP服务器",
                    .IsRunning = True,
                    .Port = 3000,
                    .Status = "运行中",
                    .StartTime = DateTime.Now.AddMinutes(-30)
                },
                New McpServiceState With {
                    .ServiceName = "远程MCP服务",
                    .IsRunning = False,
                    .Port = 8080,
                    .Status = "已停止",
                    .StartTime = Nothing
                }
            })
        End Sub
    End Class

    ''' <summary>
    ''' MCP 服务状态
    ''' </summary>
    Public Class McpServiceState
        Public Property ServiceName As String
        Public Property IsRunning As Boolean
        Public Property Port As Integer
        Public Property Status As String
        Public Property StartTime As DateTime?
    End Class
End Namespace