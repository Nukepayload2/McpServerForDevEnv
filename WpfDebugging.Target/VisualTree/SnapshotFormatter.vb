Imports System.Text
Imports System.Collections.Generic
Imports McpServerForDevEnv.WpfDebugging

' 把 SnapshotNode 树格式化成对标 a11y snapshot 的文本：
'   <2空格×depth>uid=<id> <控件类型> "<名称>" <布尔属性...> <key="value"...>
' 纯逻辑、无 WPF 依赖，可用构造的假节点单测。
'
' 属性打印约定（properties 值都是字符串）：
'   - 值 = "True"  → 直接印 key（对标 a11y 布尔属性，如 IsEnabled、IsVisible、IsFocused）
'   - 值 = "False" → 不印（布尔 false 省略，与 a11y 一致）
'   - 其他字符串   → 印 key="value"
' 行内顺序：先 uid、类型、名称，再 properties 按字典插入顺序（调用方负责挑有价值的属性并排序）。
'
' 【布尔字面量大小写敏感】比较用严格相等 "True"/"False"（Pascal 大小写），调用方（VisualTreeWalker.AddBool）
' 也按此约定填值。小写 "true"/"false" 或其他形式一律当标量处理，会被印成 key="value"。

''' <summary>
''' 把 <see cref="SnapshotNode"/> 树渲染成 a11y 风格文本快照。
''' </summary>
Public NotInheritable Class SnapshotFormatter

    Private Sub New()
    End Sub

    ''' <summary>缩进每层的空格数。</summary>
    Public Const IndentSpaces As Integer = 2

    ''' <summary>
    ''' 把整棵 <paramref name="root"/> 渲染成文本。
    ''' </summary>
    Public Shared Function Format(root As SnapshotNode) As String
        If root Is Nothing Then Return String.Empty
        Dim sb As New StringBuilder()
        AppendNode(sb, root, 0)
        Return sb.ToString()
    End Function

    Private Shared Sub AppendNode(sb As StringBuilder, node As SnapshotNode, depth As Integer)
        sb.Append(" "c, depth * IndentSpaces)

        ' uid=<id>
        sb.Append("uid=").Append(If(node.Uid, String.Empty))

        ' 类型（对标 a11y role）
        If Not String.IsNullOrEmpty(node.TypeName) Then
            sb.Append(" "c).Append(node.TypeName)
        End If

        ' 名称（对标 a11y name，带引号）
        If Not String.IsNullOrEmpty(node.Name) Then
            sb.Append(" "c).Append(""""c).Append(EscapeName(node.Name)).Append(""""c)
        End If

        ' 属性
        If node.Properties IsNot Nothing Then
            For Each kv As KeyValuePair(Of String, String) In node.Properties
                If String.IsNullOrEmpty(kv.Key) Then Continue For
                AppendAttribute(sb, kv.Key, kv.Value)
            Next
        End If

        sb.AppendLine()

        If node.Children IsNot Nothing Then
            For Each child As SnapshotNode In node.Children
                AppendNode(sb, child, depth + 1)
            Next
        End If
    End Sub

    Private Shared Sub AppendAttribute(sb As StringBuilder, key As String, value As String)
        ' null/空 → 印裸 key（存在性标记）
        If value Is Nothing Then
            sb.Append(" "c).Append(key)
            Return
        End If

        ' 布尔约定：True 印 key，False 不印
        If value = "True" Then
            sb.Append(" "c).Append(key)
            Return
        End If
        If value = "False" Then
            Return
        End If

        ' 标量：key="value"
        sb.Append(" "c).Append(key).Append("=""").Append(EscapeValue(value)).Append(""""c)
    End Sub

    ' 名称里的换行/控制字符用简短转义，避免破坏行结构。
    Private Shared Function EscapeName(name As String) As String
        If String.IsNullOrEmpty(name) Then Return name
        Return EscapeCommon(name)
    End Function

    Private Shared Function EscapeValue(value As String) As String
        If String.IsNullOrEmpty(value) Then Return value
        Return EscapeCommon(value)
    End Function

    Private Shared Function EscapeCommon(s As String) As String
        Dim sb As New StringBuilder(s.Length)
        For Each ch As Char In s
            Select Case ch
                Case "\"c
                    sb.Append("\\")            ' 反斜杠 → \\（字符串形式，两个反斜杠字符）
                Case ControlChars.Cr
                    sb.Append("\r")
                Case ControlChars.Lf
                    sb.Append("\n")
                Case ControlChars.Tab
                    sb.Append("\t")
                Case Else
                    If Char.IsControl(ch) Then
                        sb.Append("\u").Append(AscW(ch).ToString("X4", Globalization.CultureInfo.InvariantCulture))
                    Else
                        sb.Append(ch)
                    End If
            End Select
        Next
        Return sb.ToString()
    End Function
End Class
