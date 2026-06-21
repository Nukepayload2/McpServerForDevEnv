Imports System.Threading.Tasks
Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

' WPF 调试 MVP 六工具之一：列出当前被控进程的所有窗口。
'
' 【契约对照】IWpfDebugTarget.ListWindows() → IList(Of WindowInfo)
'            WpfDebugMethods.ListWindows = "list_windows"
'            WindowInfo 字段：windowId / title / isMain / handle
'
' 【参数】无（对应被控端无参方法）。
' 【返回值】窗口数组（WindowInfo），直接透传业务返回值，不做文本化（结构化对 AI 更友好）。

''' <summary>
''' 列出当前被控进程的所有窗口。
''' </summary>
Public Class ListWindowsTool
    Inherits WpfDebugToolBase

    Public Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, permissionHandler)
    End Sub

    Public Overrides ReadOnly Property Method As String = WpfDebugMethods.ListWindows

    Public Overrides ReadOnly Property ToolDefinition As New ToolDefinition With {
        .Name = "list_windows",
        .Description = "列出当前被控 WPF 进程的所有窗口（返回 windowId/标题/是否主窗口/句柄）",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition)()
        }
    }

    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel = PermissionLevel.Allow

    ''' <summary>无参方法。</summary>
    Protected Overrides Function BuildParams(arguments As Dictionary(Of String, Object)) As Object
        Return Nothing
    End Function

    ''' <summary>被控端 OkResult 包的是 WindowInfo 数组，直接作为结构化返回值透传。</summary>
    Protected Overrides Function FormatResult(payload As JToken) As Object
        ' payload 可能是 Nothing（被控端没窗口时返回空数组也会进 OkResult，不会 Nothing），
        ' 防御性兜底：Nothing 时返回空数组，保证调用方拿到的始终是数组。
        If payload Is Nothing Then Return New JArray()
        Return payload
    End Function
End Class
