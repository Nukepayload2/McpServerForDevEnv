Imports System.Collections.Generic
Imports McpServerForDevEnv.WpfDebugging

' interestingOnly 裁剪：裁掉纯装饰/无信息节点，对标 chrome a11y 的 interestingOnly。
' 纯逻辑、无 WPF 依赖，可用构造的假节点单测。
'
' 判定规则（IsInteresting）：
'   - 有非空 Name；或
'   - TypeName 命中交互控件白名单；或
'   - Properties 含交互/状态信号键（IsEnabled/IsFocused/IsVisible/IsHitTestVisible/IsKeyboardFocused）且值为 "True"。
'
' 裁剪策略（对标 a11y）：不 interesting 的节点折叠——自身消失，但其 interesting 子孙提升到父节点位置，
' 避免把有用节点连同无用装饰外壳一起裁掉。用 CollectInteresting 递归：每个节点展开成 0/1/多个节点列表，
' 父收集子列表再决定自身去留，干净处理「折叠多子提升」。根节点即使不 interesting 也保留（否则空树）。

''' <summary>
''' interestingOnly 裁剪器。
''' </summary>
Public NotInheritable Class InterestingNodeFilter

    Private Sub New()
    End Sub

    ' 交互控件类型白名单（用 GetType().Name 比较，大小写敏感，WPF 类型名首字母大写）。
    Private Shared ReadOnly InteractiveTypes As New HashSet(Of String) From {
        "Button",
        "ToggleButton",
        "RepeatButton",
        "CheckBox",
        "RadioButton",
        "TextBox",
        "PasswordBox",
        "ComboBox",
        "ComboBoxItem",
        "ListBox",
        "ListBoxItem",
        "ListView",
        "ListViewItem",
        "MenuItem",
        "Menu",
        "TabItem",
        "TabControl",
        "TreeView",
        "TreeViewItem",
        "DataGrid",
        "DataGridRow",
        "DataGridCell",
        "Slider",
        "Expander",
        "Hyperlink",
        "Thumb"
    }

    ' 标记交互/状态的属性键（值为 "True" 时认为节点 interesting）。
    Private Shared ReadOnly SignalKeys As New HashSet(Of String) From {
        "IsEnabled",
        "IsVisible",
        "IsFocused",
        "IsKeyboardFocused",
        "IsHitTestVisible"
    }

    ''' <summary>
    ''' 对 <paramref name="root"/> 做 interestingOnly 裁剪，返回新树（不修改输入）。
    ''' 根节点即使不 interesting 也保留（避免返回空树）。
    ''' </summary>
    Public Shared Function Filter(root As SnapshotNode) As SnapshotNode
        If root Is Nothing Then Return Nothing

        ' 先收集根的子（子可能折叠/提升）。
        Dim promotedChildren As IList(Of SnapshotNode) = CollectChildren(root)

        Dim copy As SnapshotNode = CloneShallow(root)
        If promotedChildren IsNot Nothing AndAlso promotedChildren.Count > 0 Then
            copy.Children = New List(Of SnapshotNode)(promotedChildren)
        Else
            copy.Children = Nothing
        End If
        Return copy
    End Function

    ''' <summary>
    ''' 递归收集一个节点的「应当出现在父的子列表里」的节点。
    ''' 一个节点：interesting → 保留自身（挂其收集后的子）；否则 → 折叠，返回提升上来的孙（可能多个/0 个）。
    ''' </summary>
    Private Shared Function CollectChildren(node As SnapshotNode) As IList(Of SnapshotNode)
        Dim result As New List(Of SnapshotNode)()
        If node Is Nothing OrElse node.Children Is Nothing Then Return result

        For Each child As SnapshotNode In node.Children
            Dim childOwnChildren As IList(Of SnapshotNode) = CollectChildren(child)
            If IsInteresting(child) Then
                Dim copy As SnapshotNode = CloneShallow(child)
                If childOwnChildren.Count > 0 Then
                    copy.Children = New List(Of SnapshotNode)(childOwnChildren)
                Else
                    copy.Children = Nothing
                End If
                result.Add(copy)
            Else
                ' 折叠：把孙提升到本层（result）。
                For Each promoted As SnapshotNode In childOwnChildren
                    result.Add(promoted)
                Next
            End If
        Next
        Return result
    End Function

    ''' <summary>判定单节点是否 interesting。</summary>
    Public Shared Function IsInteresting(node As SnapshotNode) As Boolean
        If node Is Nothing Then Return False

        If Not String.IsNullOrEmpty(node.Name) Then Return True

        If Not String.IsNullOrEmpty(node.TypeName) AndAlso InteractiveTypes.Contains(node.TypeName) Then
            Return True
        End If

        If node.Properties IsNot Nothing Then
            For Each kv As KeyValuePair(Of String, String) In node.Properties
                If kv.Value = "True" AndAlso SignalKeys.Contains(kv.Key) Then Return True
            Next
        End If

        Return False
    End Function

    Private Shared Function CloneShallow(node As SnapshotNode) As SnapshotNode
        Dim copy As New SnapshotNode With {
            .Uid = node.Uid,
            .TypeName = node.TypeName,
            .Name = node.Name
        }
        If node.Properties IsNot Nothing Then
            copy.Properties = New Dictionary(Of String, String)(node.Properties)
        End If
        Return copy
    End Function
End Class
