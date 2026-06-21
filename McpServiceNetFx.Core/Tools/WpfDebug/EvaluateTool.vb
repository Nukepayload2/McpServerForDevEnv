Imports System.Threading.Tasks
Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

' WPF 调试 MVP 六工具之五：在被控进程上下文执行 VB.NET 脚本。
'
' 【契约对照】IWpfDebugTarget.Evaluate(script, timeoutMs) → String（脚本结果字符串形式）
'            WpfDebugMethods.Evaluate = "evaluate"
'
' 【参数】script（必填，VB.NET 源）、timeoutMs?（执行超时；不指定不限）。
' 【返回值】被控端 OkResult 包的是字符串，直接返回给 AI。
'
' 【安全】evaluate 能在被控进程跑任意代码，默认权限设为 Ask（区别于其它只读工具的 Allow），
'   遵循现有 IMcpPermissionHandler 的弹框确认机制。

''' <summary>
''' 在被控 WPF 进程上下文执行一段 VB.NET 脚本并返回结果。
''' </summary>
Public Class EvaluateTool
    Inherits WpfDebugToolBase

    Public Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, permissionHandler)
    End Sub

    Public Overrides ReadOnly Property Method As String = WpfDebugMethods.Evaluate

    Public Overrides ReadOnly Property ToolDefinition As New ToolDefinition With {
        .Name = "evaluate",
        .Description = "在被控 WPF 进程上下文执行 VB.NET 脚本，返回结果字符串（脚本可访问 Application/MainWindow 等）",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition) From {
                {
                    "script",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "VB.NET 脚本源"
                    }
                },
                {
                    "timeoutMs",
                    New PropertyDefinition With {
                        .Type = "number",
                        .Description = "脚本执行超时（毫秒）；不指定表示不限"
                    }
                }
            },
            .Required = {"script"}
        }
    }

    ' 脚本能跑任意代码（可读写属性、调方法），默认 Ask 确认；其余只读工具是 Allow。
    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel = PermissionLevel.Ask

    Protected Overrides Function BuildParams(arguments As Dictionary(Of String, Object)) As Object
        ValidateRequiredArguments(arguments, "script")

        ' timeoutMs：未提供时给 Nothing（契约 Integer? Nothing 表示不限）。
        Dim timeoutMs As Integer? = Nothing
        If arguments.ContainsKey("timeoutMs") AndAlso arguments("timeoutMs") IsNot Nothing Then
            Try
                timeoutMs = CInt(Convert.ChangeType(arguments("timeoutMs"), GetType(Integer)))
            Catch ex As Exception
                LogOperation(ToolName, "参数转换失败", $"timeoutMs={arguments("timeoutMs")} 无法转为 Integer，按未指定处理")
            End Try
        End If

        Return New With {
            .script = CStr(arguments("script")),
            .timeoutMs = timeoutMs
        }
    End Function

    ''' <summary>业务返回值是脚本结果字符串。</summary>
    Protected Overrides Function FormatResult(payload As JToken) As Object
        ' payload 可能是标量（JValue，字符串被 OkResult 包成 JToken）。
        Dim result As String = Nothing
        If payload IsNot Nothing Then
            Select Case payload.Type
                Case JTokenType.String
                    result = payload.Value(Of String)()
                Case JTokenType.Null
                    result = Nothing
                Case Else
                    ' 被控端按契约只回字符串，但防御性兜底：非字符串时序列化成 JSON 给 AI 看。
                    result = payload.ToString()
            End Select
        End If
        Return New With {.result = result}
    End Function
End Class
