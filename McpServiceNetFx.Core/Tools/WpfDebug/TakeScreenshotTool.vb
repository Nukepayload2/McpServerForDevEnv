Imports System.Threading.Tasks
Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

' WPF 调试 MVP 六工具之六：截当前窗口或指定 uid 元素的图。
'
' 【契约对照】IWpfDebugTarget.TakeScreenshot(windowId, uid) → ScreenshotResult
'            WpfDebugMethods.TakeScreenshot = "take_screenshot"
'            ScreenshotResult 字段：width / height / pngBase64
'
' 【参数】windowId?（不指定截当前窗口）、uid?（不指定截整个窗口，指定则截该元素区域）。
' 【返回值】ScreenshotResult（含 base64 PNG），直接透传。

''' <summary>
''' 截取 WPF 窗口或指定 uid 元素的图像（返回 base64 PNG）。
''' </summary>
Public Class TakeScreenshotTool
    Inherits WpfDebugToolBase

    Public Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, permissionHandler)
    End Sub

    Public Overrides ReadOnly Property Method As String = WpfDebugMethods.TakeScreenshot

    Public Overrides ReadOnly Property ToolDefinition As New ToolDefinition With {
        .Name = "take_screenshot",
        .Description = "截取 WPF 窗口或指定 uid 元素的图像，返回 ScreenshotResult（含 base64 PNG）",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition) From {
                {
                    "windowId",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "目标窗口 id（来自 list_windows）；不指定截当前窗口"
                    }
                },
                {
                    "uid",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "目标元素 uid（来自 take_snapshot）；不指定截整个窗口"
                    }
                }
            }
        }
    }

    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel = PermissionLevel.Allow

    ''' <summary>两个参数都可选，空即不传（被控端契约 windowId/uid 为 Nothing 表示不指定）。</summary>
    Protected Overrides Function BuildParams(arguments As Dictionary(Of String, Object)) As Object
        Return New With {
            .windowId = GetOptionalStringOrNothing(arguments, "windowId"),
            .uid = GetOptionalStringOrNothing(arguments, "uid")
        }
    End Function

    ''' <summary>业务返回值是 ScreenshotResult，直接透传。</summary>
    Protected Overrides Function FormatResult(payload As JToken) As Object
        If payload Is Nothing Then
            Return New With {.success = False, .message = "被控端未返回截图数据"}
        End If
        Return payload
    End Function
End Class
