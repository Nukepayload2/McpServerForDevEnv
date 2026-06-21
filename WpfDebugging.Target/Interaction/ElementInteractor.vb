Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Reflection
Imports System.Windows.Automation.Peers
Imports System.Windows.Automation.Provider
Imports System.Windows.Controls
Imports System.Windows.Controls.Primitives
Imports System.Windows.Input
Imports McpServerForDevEnv.WpfDebugging

' 元素交互：click（点击）与 fill（填值）。
' 对标 chrome-devtools-mcp 的 input 工具，但操作对象是 WPF UIElement。
'
' 【线程约定】本类在 UI 线程被调用（由 WpfDebugPipeServer 经 WpfDispatcher.InvokeAsync 切入），
' 直接碰 WPF 对象，不二次切线程（见 WpfDebugTargetImpl 顶部约定）。
'
' 【OkResult 嵌套约定】本类方法只做交互/抛异常；返回值封装（包成 {"result":<JToken>}）由
' WpfDebugPipeServer.DispatchOnUiThread 的 OkResult 做。本类不碰封装。
'
' 注：本包根命名空间是 McpServerForDevEnv.WpfDebugging.Target，WPF 类型一律 Global.System.Windows.*
' 完整限定，避免「Windows」子命名空间歧义。这里 Imports 的 System.Windows.* 子命名空间（Controls/Primitives/
' Input/Automation.Peers）不与根冲突。Automation.Peers 是带后缀的子命名空间，Imports 安全。

''' <summary>
''' 元素交互器：按 uid 执行点击与填值。
''' </summary>
Public NotInheritable Class ElementInteractor

    Private Sub New()
    End Sub

    ' ===== click =====

    ''' <summary>
    ''' 点击元素。优先走 AutomationPeer.Invoke（WPF 标准「程序化点击」），拿不到 peer 或
    ''' 不支持 Invoke 时退化到构造鼠标事件做 RaiseEvent（先聚焦，再按 Preview/Down/Preview/Up
    ''' 顺序路由左键事件序列，触发 ButtonBase.Click）。
    ''' </summary>
    ''' <param name="element">目标元素（必须是 UIElement）。</param>
    Public Shared Sub Click(element As Global.System.Windows.UIElement)
        If element Is Nothing Then Throw New ArgumentNullException(NameOf(element))

        ' 1) AutomationPeer 路径：取已有 peer 或新建，能 Invoke 就走（Button/CheckBox/RadioButton 等标准控件都支持）。
        Dim peer As AutomationPeer = TryGetOrCreatePeer(element)
        If peer IsNot Nothing Then
            Dim invokePattern As Global.System.Windows.Automation.Provider.IInvokeProvider =
                TryCast(peer.GetPattern(Global.System.Windows.Automation.Peers.PatternInterface.Invoke),
                        Global.System.Windows.Automation.Provider.IInvokeProvider)
            If invokePattern IsNot Nothing Then
                invokePattern.Invoke()
                Return
            End If
        End If

        ' 2) 退化：聚焦 + 构造左键鼠标事件序列做 RaiseEvent（Preview/Down/Preview/Up），
        '    模拟真实单击事件顺序，触发 ButtonBase.Click。
        RaiseMouseClick(element)
    End Sub

    ' 取元素已有的 peer（来自其父 peer 的 GetChildren），没有则用 CreatePeerForElement 新建。
    Private Shared Function TryGetOrCreatePeer(element As Global.System.Windows.UIElement) As AutomationPeer
        Try
            Dim existing As AutomationPeer = UIElementAutomationPeer.FromElement(element)
            If existing IsNot Nothing Then Return existing
            Return New UIElementAutomationPeer(element)
        Catch
            ' 某些控件不允许建 peer（如未布局完成的元素），退化到 RaiseEvent。
            Return Nothing
        End Try
    End Function

    ' 退化路径：构造鼠标点击事件序列并路由。
    Private Shared Sub RaiseMouseClick(element As Global.System.Windows.UIElement)
        ' 先聚焦，让 GotFocus/GotKeyboardFocus 等附属事件按真实点击顺序触发。
        Try
            element.Focus()
        Catch
        End Try

        Dim device As MouseDevice = Mouse.PrimaryDevice
        ' Mouse.PrimaryDevice 在无输入上下文时可能为 Nothing，此路径下退而求其次：只发 Click 路由事件。
        If device Is Nothing Then
            element.RaiseEvent(New Global.System.Windows.RoutedEventArgs(Global.System.Windows.Controls.Primitives.ButtonBase.ClickEvent, element))
            Return
        End If

        Dim timestamp As Integer = Environment.TickCount

        ' MouseButtonEventArgs(MouseDevice, timestamp, changedButton)：左键。
        ' Button 的 Click 由 ButtonBase 在 OnMouseLeftButtonUp 里触发，所以完整发 Preview/Down/Preview/Up
        ' 才能让 Button 抬起时点一下。隧道事件（Preview*）先于冒泡事件（*）发，模拟真实点击顺序。
        ' MouseLeftButton*Event 是 UIElement 暴露的路由事件（不是 Mouse 类的静态成员）。
        element.RaiseEvent(New MouseButtonEventArgs(device, timestamp, MouseButton.Left) With {
            .RoutedEvent = Global.System.Windows.UIElement.PreviewMouseLeftButtonDownEvent,
            .Source = element})
        element.RaiseEvent(New MouseButtonEventArgs(device, timestamp, MouseButton.Left) With {
            .RoutedEvent = Global.System.Windows.UIElement.MouseLeftButtonDownEvent,
            .Source = element})
        element.RaiseEvent(New MouseButtonEventArgs(device, timestamp, MouseButton.Left) With {
            .RoutedEvent = Global.System.Windows.UIElement.PreviewMouseLeftButtonUpEvent,
            .Source = element})
        element.RaiseEvent(New MouseButtonEventArgs(device, timestamp, MouseButton.Left) With {
            .RoutedEvent = Global.System.Windows.UIElement.MouseLeftButtonUpEvent,
            .Source = element})
    End Sub

    ' ===== fill =====

    ''' <summary>
    ''' 给控件填值。按控件类型分流：<see cref="TextBox"/>.<see cref="TextBox.Text"/>、
    ''' <see cref="PasswordBox"/>.<see cref="PasswordBox.Password"/>、
    ''' <see cref="ComboBox"/>.<see cref="ComboBox.SelectedValue"/>（退化 Text）、
    ''' <see cref="CheckBox"/>/<see cref="RadioButton"/>.<see cref="ToggleButton.IsChecked"/>、
    ''' <see cref="RangeBase"/>（Slider/ProgressBar 等）.<see cref="RangeBase.Value"/>，
    ''' 其它退化到反射找可写 Text/Content 属性。不支持或只读的控件抛明确错误。
    ''' </summary>
    ''' <param name="target">目标控件。</param>
    ''' <param name="value">要填的值（字符串形式）。</param>
    Public Shared Sub Fill(target As Global.System.Windows.UIElement, value As String)
        If target Is Nothing Then Throw New ArgumentNullException(NameOf(target))

        ' 先按具体类型分流；分流命中的类型走强类型路径，类型名不匹配时退化反射。
        Dim applied As Boolean = ApplyByType(target, value)
        If applied Then Return

        ' 退化：反射找可写的 Text / Content 属性，找不到/只读抛错。
        If TryReflectSet(target, "Text", value) Then Return
        If TryReflectSet(target, "Content", value) Then Return

        Throw New InvalidOperationException(
            $"控件 {target.GetType().Name} 不支持 fill（既不是已知输入控件，也没有可写的 Text/Content 属性）。")
    End Sub

    ''' <summary>
    ''' 按控件具体类型分流填值。命中已知类型返回 True；类型不在分流表里返回 False（由调用方退化）。
    ''' </summary>
    Private Shared Function ApplyByType(target As Global.System.Windows.UIElement, value As String) As Boolean
        ' TextBox：直接设 Text。
        Dim tb As TextBox = TryCast(target, TextBox)
        If tb IsNot Nothing Then
            tb.Text = If(value, String.Empty)
            Return True
        End If

        ' PasswordBox：设 Password（独立于 Text，不能用反射 Text 命中）。
        Dim pb As PasswordBox = TryCast(target, PasswordBox)
        If pb IsNot Nothing Then
            pb.Password = If(value, String.Empty)
            Return True
        End If

        ' ComboBox：优先 SelectedValue（绑定场景）；退化到 Text（直接文本）。
        Dim cb As ComboBox = TryCast(target, ComboBox)
        If cb IsNot Nothing Then
            If ApplyComboBox(cb, value) Then Return True
            ' SelectedValue 走不通（只读/无匹配项）→ 退化 Text。
            cb.Text = If(value, String.Empty)
            Return True
        End If

        ' CheckBox / RadioButton / ToggleButton：设 IsChecked（"true"/"false"/"1"/"0"/"yes"/"no" 解析）。
        Dim toggle As ToggleButton = TryCast(target, ToggleButton)
        If toggle IsNot Nothing Then
            toggle.IsChecked = ParseBoolean(value)
            Return True
        End If

        ' Slider / ProgressBar 等 RangeBase：设 Value（double 解析）。
        Dim range As RangeBase = TryCast(target, RangeBase)
        If range IsNot Nothing Then
            range.Value = ParseDouble(value, range.Value)
            Return True
        End If

        Return False
    End Function

    ' ComboBox 设 SelectedValue：先按字符串相等找匹配项；找不到回退尝试把 value 当 SelectedIndex/SelectedValue 原值。
    ' 设不成功（如 IsReadOnly 或无匹配）返回 False，由调用方退化到 Text。
    Private Shared Function ApplyComboBox(cb As ComboBox, value As String) As Boolean
        If cb.IsReadOnly Then Return False
        Try
            ' 1) 在 Items 里找 SelectedValuePath 指向属性等于 value 的项，或 ToString 等于 value 的项。
            Dim svp As String = cb.SelectedValuePath
            For Each item As Object In cb.Items
                If item Is Nothing Then Continue For
                Dim candidate As String = If(String.IsNullOrEmpty(svp),
                                             ConvertToString(item),
                                             If(ReadPathValue(item, svp), ConvertToString(item)))
                If String.Equals(candidate, value, StringComparison.Ordinal) Then
                    cb.SelectedItem = item
                    Return True
                End If
            Next

            ' 2) 当作数字索引。
            Dim idx As Integer
            If Integer.TryParse(value, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture, idx) _
               AndAlso idx >= 0 AndAlso idx < cb.Items.Count Then
                cb.SelectedIndex = idx
                Return True
            End If

            ' 3) 直接赋 SelectedValue（绑定到枚举/数字时 value 是其字符串形式，由类型转换器兜底）。
            cb.SelectedValue = value
            Return True
        Catch
            Return False
        End Try
    End Function

    ' 反射设可写 CLR/DP 属性。找不到、只读、类型不兼容返回 False。
    Private Shared Function TryReflectSet(target As Global.System.Windows.UIElement, propName As String, value As String) As Boolean
        Try
            Dim pi As PropertyInfo = target.GetType().GetProperty(propName,
                BindingFlags.Public Or BindingFlags.Instance)
            If pi Is Nothing OrElse Not pi.CanWrite Then Return False
            If pi.GetIndexParameters().Length > 0 Then Return False
            Dim converted As Object = ConvertToPropertyType(pi.PropertyType, value)
            If converted Is Nothing AndAlso pi.PropertyType.IsValueType Then
                ' 类型不兼容（ConvertToPropertyType 返回 Nothing 且属性是值类型）。
                Return False
            End If
            pi.SetValue(target, converted)
            Return True
        Catch
            Return False
        End Try
    End Function

    ' 把字符串 value 转成属性类型。不兼容返回 Nothing（调用方按属性是否值类型判断）。
    Private Shared Function ConvertToPropertyType(propType As Type, value As String) As Object
        Try
            If propType Is GetType(String) Then Return value
            Dim converter As TypeConverter = TypeDescriptor.GetConverter(propType)
            If converter IsNot Nothing AndAlso converter.CanConvertFrom(GetType(String)) Then
                Return converter.ConvertFromString(Nothing, Globalization.CultureInfo.InvariantCulture, value)
            End If
            Return Nothing
        Catch
            Return Nothing
        End Try
    End Function

    ' 通用辅助：取对象字符串形式（无副作用，不碰 UI 线程）。
    Private Shared Function ConvertToString(obj As Object) As String
        If obj Is Nothing Then Return Nothing
        Return If(TryCast(obj, String), obj.ToString())
    End Function

    ' 反射读对象上某属性路径的值（SelectedValuePath 形如 "Id"/"Value.Key"，点分段）。
    Private Shared Function ReadPathValue(item As Object, path As String) As String
        Dim current As Object = item
        For Each part As String In path.Split("."c)
            If current Is Nothing Then Return Nothing
            Dim pi As PropertyInfo = current.GetType().GetProperty(part,
                BindingFlags.Public Or BindingFlags.Instance)
            If pi Is Nothing OrElse Not pi.CanRead OrElse pi.GetIndexParameters().Length > 0 Then Return Nothing
            current = pi.GetValue(current)
        Next
        Return ConvertToString(current)
    End Function

    Private Shared Function ParseBoolean(value As String) As Boolean?
        If String.IsNullOrEmpty(value) Then Return Nothing
        Dim v As String = value.Trim().ToLowerInvariant()
        Select Case v
            Case "true", "1", "yes", "y", "on", "checked"
                Return True
            Case "false", "0", "no", "n", "off", "unchecked"
                Return False
            Case Else
                Return Nothing
        End Select
    End Function

    Private Shared Function ParseDouble(value As String, fallback As Double) As Double
        Dim d As Double
        If Double.TryParse(value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, d) Then
            Return d
        End If
        Return fallback
    End Function

    ' ===== 可测纯函数：控件类型 → fill 设置策略 =====
    ' 把「控件类型 → 该走哪个属性/分支」抽成不碰 UI 线程的纯函数，便于单测覆盖各分支。
    ' 输入是控件类型名（GetType().Name），输出是命中的设置策略。

    ''' <summary>
    ''' 根据控件类型名判定 fill 的设置策略。纯函数，不碰任何 WPF 对象/线程。
    ''' </summary>
    ''' <returns>
    ''' 设置策略：<see cref="FillStrategy.Text"/>（TextBox）/ <see cref="FillStrategy.Password"/>（PasswordBox）/
    ''' <see cref="FillStrategy.SelectedValue"/>（ComboBox）/ <see cref="FillStrategy.IsChecked"/>（CheckBox/RadioButton/ToggleButton）/
    ''' <see cref="FillStrategy.RangeValue"/>（Slider/ProgressBar 等 RangeBase 派生）/ <see cref="FillStrategy.ReflectFallback"/>（退化反射 Text/Content）。
    ''' </returns>
    Public Shared Function ResolveFillStrategy(typeName As String) As FillStrategy
        Select Case typeName
            Case NameOf(TextBox) : Return FillStrategy.Text
            Case NameOf(PasswordBox) : Return FillStrategy.Password
            Case NameOf(ComboBox) : Return FillStrategy.SelectedValue
            Case NameOf(CheckBox), NameOf(RadioButton), NameOf(ToggleButton) : Return FillStrategy.IsChecked
            Case NameOf(Slider), NameOf(ProgressBar), NameOf(ScrollBar) : Return FillStrategy.RangeValue
            Case Else : Return FillStrategy.ReflectFallback
        End Select
    End Function
End Class

''' <summary>
''' fill 按控件类型分流的设置策略（供 <see cref="ElementInteractor.ResolveFillStrategy"/> 返回）。
''' </summary>
Public Enum FillStrategy
    ''' <summary>TextBox.Text。</summary>
    [Text]
    ''' <summary>PasswordBox.Password。</summary>
    Password
    ''' <summary>ComboBox.SelectedValue / 退化 Text。</summary>
    SelectedValue
    ''' <summary>CheckBox/RadioButton/ToggleButton.IsChecked。</summary>
    IsChecked
    ''' <summary>Slider/ProgressBar/ScrollBar 等 RangeBase.Value。</summary>
    RangeValue
    ''' <summary>未知类型，退化反射 Text/Content。</summary>
    ReflectFallback
End Enum
