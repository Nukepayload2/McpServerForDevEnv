Imports System.Text
Imports System.Threading.Tasks
Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

' WPF 调试 MVP 六工具之二：拍指定窗口的可视树快照。
'
' 【契约对照】IWpfDebugTarget.TakeSnapshot(windowId, maxDepth, interestingOnly) → SnapshotNode
'            WpfDebugMethods.TakeSnapshot = "take_snapshot"
'            SnapshotNode 字段：uid / typeName / name / properties / children
'
' 【参数】
'   windowId?        目标窗口 id；不指定拍当前窗口。
'   maxDepth?        最大遍历深度；不指定不限。
'   interestingOnly? 是否裁掉纯装饰节点（对标 chrome 的 interestingOnly）；默认 false。
' 【返回值】文本快照（缩进树，每节点一行）+ 结构化 SnapshotNode 树（见 rough-plan 第七节）。
'   MCP 返回匿名对象 { text, snapshot }，text 给 AI 看，snapshot 保留 uid 供后续 click/fill 用。

''' <summary>
''' 拍指定 WPF 窗口的可视树快照（文本 + 结构化双形态）。
''' </summary>
Public Class TakeSnapshotTool
    Inherits WpfDebugToolBase

    Public Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, permissionHandler)
    End Sub

    Public Overrides ReadOnly Property Method As String = WpfDebugMethods.TakeSnapshot

    Public Overrides ReadOnly Property ToolDefinition As New ToolDefinition With {
        .Name = "take_snapshot",
        .Description = "拍指定 WPF 窗口的可视树快照，返回文本树（缩进每层2空格）和结构化 SnapshotNode",
        .InputSchema = New InputSchema With {
            .Type = "object",
            .Properties = New Dictionary(Of String, PropertyDefinition) From {
                {
                    "windowId",
                    New PropertyDefinition With {
                        .Type = "string",
                        .Description = "目标窗口 id（来自 list_windows）；不指定拍当前窗口"
                    }
                },
                {
                    "maxDepth",
                    New PropertyDefinition With {
                        .Type = "number",
                        .Description = "最大遍历深度；不指定表示不限"
                    }
                },
                {
                    "interestingOnly",
                    New PropertyDefinition With {
                        .Type = "boolean",
                        .Description = "是否裁掉纯装饰节点（对标 chrome 的 interestingOnly）",
                        .[Default] = "false"
                    }
                }
            }
        }
    }

    Public Overrides ReadOnly Property DefaultPermission As PermissionLevel = PermissionLevel.Allow

    ''' <summary>把 MCP 入参翻译成被控端参数对象。可选参数空即不传。</summary>
    Protected Overrides Function BuildParams(arguments As Dictionary(Of String, Object)) As Object
        ' maxDepth：未提供或非法时给 Nothing（被控端契约 Integer? Nothing 表示不限）。
        Dim maxDepth As Integer? = Nothing
        If arguments IsNot Nothing AndAlso arguments.ContainsKey("maxDepth") AndAlso arguments("maxDepth") IsNot Nothing Then
            Try
                maxDepth = CInt(Convert.ChangeType(arguments("maxDepth"), GetType(Integer)))
            Catch ex As Exception
                ' 非法值当未提供处理，沿用基类 GetOptionalArgument 的容错风格。
                LogOperation(ToolName, "参数转换失败", $"maxDepth={arguments("maxDepth")} 无法转为 Integer，按未指定处理")
            End Try
        End If

        Return New With {
            .windowId = GetOptionalStringOrNothing(arguments, "windowId"),
            .maxDepth = maxDepth,
            .interestingOnly = GetOptionalArgument(arguments, "interestingOnly", False)
        }
    End Function

    ''' <summary>业务返回值是 SnapshotNode（根节点），格式化成 { text, snapshot }。</summary>
    Protected Overrides Function FormatResult(payload As JToken) As Object
        If payload Is Nothing Then
            Return New With {.text = "(空快照)", .snapshot = Nothing}
        End If

        Dim text As String = SnapshotFormatter.Format(payload)
        Return New With {.text = text, .snapshot = payload}
    End Function
End Class

''' <summary>
''' 把 SnapshotNode 的 JToken 渲染成缩进文本树。纯函数，无副作用，可单测。
''' 对标 chrome a11y snapshot 的「缩进树 + 每节点一行」格式（见 rough-plan 第七节）。
''' </summary>
Public NotInheritable Class SnapshotFormatter

    Private Sub New()
    End Sub

    ''' <summary>
    ''' 渲染 SnapshotNode（JToken 形态）成缩进文本。每节点一行，缩进每层 2 空格。
    ''' 格式：<c>uid=&lt;id&gt; &lt;类型&gt; "&lt;名称&gt;" &lt;属性...&gt;</c>。
    ''' </summary>
    Public Shared Function Format(node As JToken) As String
        If node Is Nothing Then Return ""
        Dim sb As New StringBuilder()
        AppendNode(sb, node, 0)
        Return sb.ToString()
    End Function

    Private Shared Sub AppendNode(sb As StringBuilder, node As JToken, depth As Integer)
        If node Is Nothing Then Return

        ' 缩进每层 2 空格。
        sb.Append(New String(" "c, depth * 2))

        ' uid=<id>
        Dim uid As String = Nothing
        Dim uidToken As JToken = node("uid")
        If uidToken IsNot Nothing Then uid = uidToken.Value(Of String)()
        sb.Append("uid=").Append(uid)

        ' <类型>
        Dim typeName As String = Nothing
        Dim typeToken As JToken = node("typeName")
        If typeToken IsNot Nothing Then typeName = typeToken.Value(Of String)()
        sb.Append(" ").Append(typeName)

        ' "名称"（仅当有 name 字段）
        Dim nameToken As JToken = node("name")
        If nameToken IsNot Nothing Then
            Dim name As String = nameToken.Value(Of String)()
            If Not String.IsNullOrEmpty(name) Then
                sb.Append(" """).Append(name).Append("""")
            End If
        End If

        ' 属性：key=value 形式追加（对标 a11y snapshot 的属性行）。
        Dim props As JToken = node("properties")
        If props IsNot Nothing AndAlso props.Type = JTokenType.Object Then
            For Each prop As KeyValuePair(Of String, JToken) In CType(props, JObject)
                sb.Append(" ").Append(prop.Key).Append("=").Append(prop.Value.ToString())
            Next
        End If

        sb.AppendLine()

        ' 递归子节点。
        Dim children As JToken = node("children")
        If children IsNot Nothing AndAlso children.Type = JTokenType.Array Then
            For Each child As JToken In children
                AppendNode(sb, child, depth + 1)
            Next
        End If
    End Sub
End Class
