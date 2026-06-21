Imports System.Threading.Tasks
Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

' WPF 调试 MVP 六工具之三：按 uid 点击元素。
'
' 【契约对照】IWpfDebugTarget.Click(uid)（Sub，无返回值）
'            WpfDebugMethods.Click = "click"
'
' 【参数】uid（必填，来自 take_snapshot 的节点 uid）。
' 【返回值】被控端 OkResult 是空 JObject（Sub 方法），payload 为 Nothing；
'   返回成功提示字符串，告诉 AI 已点击。

''' <summary>
''' 按 uid 点击 WPF 元素。
''' </summary>
Public Class ClickTool
    Inherits WpfDebugToolBase

    Public Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, permissionHandler)
    End Sub

    Public Overrides ReadOnly Property Method As String = WpfDebugMethods.Click

    Public Overrides ReadOnly Property ToolDefinition As New ToolDefinition With {
        .Name = "click",
        .Description = "按 uid 点击 WPF 元素（uid 来自 take_snapshot）",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition) From {
                {
                    "uid",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "目标元素 uid（来自 take_snapshot）"
                    }
                }
            },
            .Required = {"uid"}
        }
    }

    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel = PermissionLevel.Allow

    ''' <summary>必填参数校验沿用基类 ValidateRequiredArguments 风格。</summary>
    Protected Overrides Function BuildParams(arguments As Dictionary(Of String, Object)) As Object
        ValidateRequiredArguments(arguments, "uid")
        Return New With {.uid = CStr(arguments("uid"))}
    End Function

    ''' <summary>Sub 方法无业务返回值，给 AI 一个成功确认。</summary>
    Protected Overrides Function FormatResult(payload As JToken) As Object
        Return New With {.success = True, .message = "已点击元素"}
    End Function
End Class
