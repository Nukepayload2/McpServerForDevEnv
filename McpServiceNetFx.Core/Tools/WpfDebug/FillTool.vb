Imports System.Threading.Tasks
Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

' WPF 调试 MVP 六工具之四：按 uid 给控件填值。
'
' 【契约对照】IWpfDebugTarget.Fill(uid, value)（Sub，无返回值）
'            WpfDebugMethods.Fill = "fill"
'            被控端按控件类型分流：TextBox.Text / ComboBox.SelectedValue /
'            CheckBox.IsChecked / RadioButton.IsChecked（见 rough-plan 第七节）。
'
' 【参数】uid（必填）、value（必填）。
' 【返回值】Sub 方法无业务返回值，返回成功提示。

''' <summary>
''' 按 uid 给 WPF 控件填值（按控件类型分流）。
''' </summary>
Public Class FillTool
    Inherits WpfDebugToolBase

    Public Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, permissionHandler)
    End Sub

    Public Overrides ReadOnly Property Method As String = WpfDebugMethods.Fill

    Public Overrides ReadOnly Property ToolDefinition As New ToolDefinition With {
        .Name = "fill",
        .Description = "按 uid 给 WPF 控件填值（TextBox/ComboBox/CheckBox/RadioButton 分流）",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition) From {
                {
                    "uid",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "目标元素 uid（来自 take_snapshot）"
                    }
                },
                {
                    "value",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "要填的值（按控件类型分流）"
                    }
                }
            },
            .Required = {"uid", "value"}
        }
    }

    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel = PermissionLevel.Allow

    Protected Overrides Function BuildParams(arguments As Dictionary(Of String, Object)) As Object
        ValidateRequiredArguments(arguments, "uid", "value")
        Return New With {
            .uid = CStr(arguments("uid")),
            .value = CStr(arguments("value"))
        }
    End Function

    ''' <summary>Sub 方法无业务返回值，给 AI 一个成功确认。</summary>
    Protected Overrides Function FormatResult(payload As JToken) As Object
        Return New With {.success = True, .message = "已填值"}
    End Function
End Class
