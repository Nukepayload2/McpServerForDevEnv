Imports System.Collections.Generic
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Controls.Primitives
Imports System.Windows.Documents
Imports System.Windows.Media
Imports McpServerForDevEnv.WpfDebugging

' 可视树遍历：VisualTreeHelper + AdornerLayer + Popup，产出 SnapshotNode 树，登记 uid。
' 对标 WPFVisualTreeMcp 的 TreeWalker.GetAllVisualChildren 三路子节点收集，但对标 chrome a11y 的输出格式。
'
' 【线程约定】本类在 UI 线程被调用（由 WpfDebugPipeServer 经 WpfDispatcher.InvokeAsync 切入），
' 直接碰 WPF 对象，不二次切线程（见 WpfDebugTargetImpl 顶部约定）。
'
' 注：本包根命名空间是 McpServerForDevEnv.WpfDebugging.Target，WPF 类型一律 Global.System.Windows.* 完整限定，
' 避免「Windows」子命名空间歧义。这里 Imports 的 System.Windows.* 子命名空间（Media/Controls/...）不与根冲突。

''' <summary>
''' 可视树遍历器：把一棵可视树转成 <see cref="SnapshotNode"/> 树并登记 uid。
''' </summary>
Public NotInheritable Class VisualTreeWalker

    ' 节点输出上限（防爆栈/超大树），对标 chrome snapshot 上限思路。
    Public Const MaxNodes As Integer = 100000

    Private ReadOnly _uidRegistry As UidRegistry
    Private _nodeCount As Integer

    Public Sub New(uidRegistry As UidRegistry)
        If uidRegistry Is Nothing Then Throw New ArgumentNullException(NameOf(uidRegistry))
        _uidRegistry = uidRegistry
    End Sub

    ''' <summary>
    ''' 从 <paramref name="root"/> 开始遍历，返回 SnapshotNode 树。
    ''' </summary>
    ''' <param name="root">遍历起点（通常是窗口的根 Visual）。</param>
    ''' <param name="maxDepth">最大深度（root 为 0 层）；Nothing 表示不限（受 MaxNodes 兜底）。</param>
    ''' <param name="interestingOnly">是否裁掉纯装饰节点。</param>
    Public Function Walk(root As DependencyObject, Optional maxDepth As Integer? = Nothing, Optional interestingOnly As Boolean = False) As SnapshotNode
        If root Is Nothing Then Return Nothing
        _nodeCount = 0
        Dim node As SnapshotNode = BuildNode(root, depth:=0, maxDepth:=maxDepth)
        If node Is Nothing Then Return Nothing
        If interestingOnly Then
            node = InterestingNodeFilter.Filter(node)
        End If
        Return node
    End Function

    Private Function BuildNode(element As DependencyObject, depth As Integer, maxDepth As Integer?) As SnapshotNode
        If element Is Nothing Then Return Nothing
        If _nodeCount >= MaxNodes Then Return Nothing
        _nodeCount += 1

        Dim node As New SnapshotNode With {
            .Uid = _uidRegistry.GetOrCreateUid(element),
            .TypeName = element.GetType().Name
        }

        Dim fe As FrameworkElement = TryCast(element, FrameworkElement)
        node.Name = ResolveName(element, fe)
        node.Properties = CollectProperties(element, fe)

        ' 子节点：maxDepth 限制（root depth=0，maxDepth=1 表示只到 root 的直接子）。
        Dim childDepth As Integer = depth + 1
        If maxDepth.HasValue AndAlso childDepth > maxDepth.Value Then
            Return node
        End If

        Dim children As New List(Of SnapshotNode)()
        For Each child As DependencyObject In GetVisualChildren(element)
            Dim childNode As SnapshotNode = BuildNode(child, childDepth, maxDepth)
            If childNode IsNot Nothing Then children.Add(childNode)
        Next
        If children.Count > 0 Then
            node.Children = children
        End If
        Return node
    End Function

    ' 三路子节点收集（参考 WPFVisualTreeMcp）：标准可视子 + AdornerLayer + Popup 独立树。
    Private Iterator Function GetVisualChildren(element As DependencyObject) As IEnumerable(Of DependencyObject)
        ' 1) VisualTreeHelper 标准子
        Dim visual As Visual = TryCast(element, Visual)
        If visual IsNot Nothing Then
            Dim count As Integer = VisualTreeHelper.GetChildrenCount(visual)
            For i As Integer = 0 To count - 1
                Dim child As DependencyObject = VisualTreeHelper.GetChild(visual, i)
                If child IsNot Nothing Then Yield child
            Next
        End If

        ' 2) AdornerLayer 上的 adorners（Fluent.Ribbon Backstage 这类挂在这里，纯 VisualTreeHelper 看不到）
        Dim adorned As Visual = TryCast(element, Visual)
        If adorned IsNot Nothing Then
            Dim layer As AdornerLayer = AdornerLayer.GetAdornerLayer(adorned)
            If layer IsNot Nothing Then
                Dim adorners As Adorner() = layer.GetAdorners(adorned)
                If adorners IsNot Nothing Then
                    For Each a As Adorner In adorners
                        If a IsNot Nothing Then Yield a
                    Next
                End If
            End If
        End If

        ' 3) Popup 的独立可视树（Popup.Child 是另一棵树的根）
        Dim popup As Popup = TryCast(element, Popup)
        If popup IsNot Nothing Then
            Dim child As UIElement = popup.Child
            If child IsNot Nothing Then Yield child
        End If
    End Function

    ' 名称解析：优先 x:Name/Name，退而 Content/Text/Headers 摘要。
    Private Shared Function ResolveName(element As DependencyObject, fe As FrameworkElement) As String
        If fe IsNot Nothing AndAlso Not String.IsNullOrEmpty(fe.Name) Then
            Return fe.Name
        End If

        ' ContentControl / ButtonBase 的 Content
        Dim content As Object = TryReadProperty(element, "Content")
        If content Is Nothing Then content = TryReadProperty(element, "Text")
        If content Is Nothing Then content = TryReadProperty(element, "Header")
        If content Is Nothing Then content = TryReadProperty(element, "Title")
        If content Is Nothing Then Return Nothing

        Dim s As String = TryCast(content, String)
        If s IsNot Nothing Then Return Truncate(s)

        ' 非字符串 Content 取类型名（如 Content=某 ViewModel），给个可读摘要
        Return Truncate(content.GetType().Name)
    End Function

    ' 挑有调试价值的属性（对标 a11y 挑选）。布尔用 "True"/"False" 字符串配合 SnapshotFormatter 约定。
    Private Shared Function CollectProperties(element As DependencyObject, fe As FrameworkElement) As IDictionary(Of String, String)
        Dim props As New Dictionary(Of String, String)

        AddBool(props, element, "IsEnabled")
        AddBool(props, element, "IsVisible")
        AddBool(props, element, "IsFocused")
        AddBool(props, element, "IsKeyboardFocused")
        AddBool(props, element, "IsHitTestVisible")

        If fe IsNot Nothing Then
            ' Visibility 默认 Visible 省略（对标 a11y 默认值省略），非默认才显式印。
            If fe.Visibility <> Visibility.Visible Then
                props("Visibility") = fe.Visibility.ToString()
            End If
            ' 布局摘要（只读、有调试价值）
            If fe.ActualWidth > 0 OrElse fe.ActualHeight > 0 Then
                props("ActualSize") = $"{CInt(fe.ActualWidth)}x{CInt(fe.ActualHeight)}"
            End If
        End If

        If props.Count = 0 Then Return Nothing
        Return props
    End Function

    Private Shared Sub AddBool(props As Dictionary(Of String, String), element As DependencyObject, propName As String)
        Dim val As Object = TryReadProperty(element, propName)
        If TypeOf val Is Boolean Then
            props(propName) = If(CBool(val), "True", "False")
        End If
    End Sub

    ' 反射安全读属性（DependencyObject / 普通 CLR 属性都覆盖）。
    Private Shared Function TryReadProperty(element As DependencyObject, propName As String) As Object
        Try
            Dim pi As System.Reflection.PropertyInfo = element.GetType().GetProperty(propName, System.Reflection.BindingFlags.Public Or System.Reflection.BindingFlags.Instance Or System.Reflection.BindingFlags.NonPublic)
            If pi Is Nothing OrElse Not pi.CanRead Then Return Nothing
            ' 索引属性或无参 get 才调
            If pi.GetIndexParameters().Length > 0 Then Return Nothing
            Return pi.GetValue(element)
        Catch
            Return Nothing
        End Try
    End Function

    Private Shared Function Truncate(s As String, Optional max As Integer = 40) As String
        If s Is Nothing Then Return Nothing
        If s.Length <= max Then Return s
        Return s.Substring(0, max) & "…"
    End Function
End Class
