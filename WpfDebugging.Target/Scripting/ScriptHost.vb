Imports System.Collections.Generic
Imports System.Reflection
Imports System.Text

' 脚本宿主上下文：作为脚本的全局对象（Globals）注入脚本编译。
' AI 写的脚本就像在被控程序里写代码——通过这个 host 访问 Application / 窗口 / 元素 / 属性读写 / 日志。
' 设计对标 rough-plan 第八节「脚本上下文注入」。
'
' 【线程约定】ScriptHost 实例在 evaluate 整体已被切到 UI 线程后构造，
' 它的所有方法都假定在 UI 线程被调用（脚本执行在 UI 线程，约定见 WpfDebugTargetImpl）。
' 故 Element/GetProperty/SetProperty 直接调 UidRegistry.Resolve，不二次切线程。
'
' 【超时协作】ScriptEngine 把一个「超时检查器」传进来，host 的方法每次被脚本调用时检查；
' 一旦超时即抛 ScriptTimeoutException，让执行栈自然展开退出（参考 rough-plan 第十一节 4 软超时思路）。

''' <summary>
''' 脚本执行宿主。注入给 VB.NET 脚本，让其像在被控程序里写代码一样操作 WPF。
''' </summary>
Public NotInheritable Class ScriptHost

    Private ReadOnly _uidRegistry As UidRegistry
    Private ReadOnly _timeoutChecker As Func(Of Boolean)
    Private ReadOnly _log As Action(Of String)

    Public Sub New(uidRegistry As UidRegistry,
                   timeoutChecker As Func(Of Boolean),
                   logSink As Action(Of String))
        If uidRegistry Is Nothing Then Throw New ArgumentNullException(NameOf(uidRegistry))
        ' timeoutChecker / logSink 可为 Nothing（脚本不强制使用）。
        _uidRegistry = uidRegistry
        _timeoutChecker = timeoutChecker
        _log = logSink
    End Sub

    ' ===== 暴露给脚本的全局 API =====

    ''' <summary>
    ''' WPF Application.Current（被控进程的 Application 实例）。
    ''' 脚本里写 <c>host.Application.MainWindow.Title</c> 即可访问。
    ''' </summary>
    Public ReadOnly Property Application As Global.System.Windows.Application
        Get
            CheckTimeout()
            Return Global.System.Windows.Application.Current
        End Get
    End Property

    ''' <summary>
    ''' 主窗口（Application.Current.MainWindow）。Nothing 表示无主窗口。
    ''' </summary>
    Public ReadOnly Property MainWindow As Global.System.Windows.Window
        Get
            CheckTimeout()
            Dim app As Global.System.Windows.Application = Global.System.Windows.Application.Current
            Return app?.MainWindow
        End Get
    End Property

    ''' <summary>
    ''' 活动窗口（当前鼠标/键盘焦点所在窗口）。退化到 MainWindow。
    ''' </summary>
    Public ReadOnly Property ActiveWindow As Global.System.Windows.Window
        Get
            CheckTimeout()
            Dim focused As Global.System.Windows.Window = TryCast(
                Global.System.Windows.Input.Keyboard.FocusedElement, Global.System.Windows.Window)
            If focused Is Nothing Then
                ' 找焦点元素所在的 Window。
                Dim fe As Global.System.Windows.DependencyObject = TryCast(
                    Global.System.Windows.Input.Keyboard.FocusedElement, Global.System.Windows.DependencyObject)
                focused = If(fe IsNot Nothing, Global.System.Windows.Window.GetWindow(fe), Nothing)
            End If
            If focused Is Nothing Then focused = Global.System.Windows.Application.Current?.MainWindow
            Return focused
        End Get
    End Property

    ''' <summary>
    ''' 按 uid 取元素（DependencyObject）。uid 失效（GC/不在树）抛 InvalidOperationException，
    ''' 提示重新 take_snapshot（与 click/fill 的错误风格一致）。
    ''' </summary>
    Public Function Element(uid As String) As Global.System.Windows.DependencyObject
        CheckTimeout()
        If String.IsNullOrEmpty(uid) Then
            Throw New ArgumentNullException(NameOf(uid))
        End If
        Dim resolved As Global.System.Windows.DependencyObject = _uidRegistry.Resolve(uid)
        If resolved Is Nothing Then
            Throw New InvalidOperationException(
                $"uid={uid} 已失效（对象被 GC 或不在当前可视树）。请重新调用 take_snapshot 获取新的 uid。")
        End If
        Return resolved
    End Function

    ''' <summary>
    ''' 读元素的属性值（CLR/依赖属性皆可）。返回值的字符串形式（便于脚本直接用）。
    ''' 找不到属性、不可读抛明确异常。
    ''' </summary>
    Public Function GetProperty(uid As String, propertyName As String) As String
        CheckTimeout()
        Dim element As Global.System.Windows.DependencyObject = Me.Element(uid)
        Return ReadPropertyAsString(element, propertyName)
    End Function

    ''' <summary>
    ''' 写元素的属性值（CLR/依赖属性皆可）。value 字符串按属性类型转换。
    ''' 找不到属性、不可写、类型不兼容抛明确异常。
    ''' </summary>
    Public Sub SetProperty(uid As String, propertyName As String, value As String)
        CheckTimeout()
        Dim element As Global.System.Windows.DependencyObject = Me.Element(uid)
        WriteProperty(element, propertyName, value)
    End Sub

    ''' <summary>
    ''' 输出一条日志消息（被 ScriptEngine 收集，附加到 evaluate 结果里）。
    ''' 脚本里写 <c>host.Log("got here")</c>。
    ''' </summary>
    Public Sub Log(message As String)
        CheckTimeout()
        _log?.Invoke(If(message, String.Empty))
    End Sub

    ' ===== 内部：超时检查 =====

    Private Sub CheckTimeout()
        If _timeoutChecker IsNot Nothing AndAlso _timeoutChecker() Then
            Throw New ScriptTimeoutException()
        End If
    End Sub

    ' ===== 属性读写辅助（反射，覆盖 CLR 属性 + 依赖属性）=====

    Private Shared Function ReadPropertyAsString(element As Global.System.Windows.DependencyObject, propertyName As String) As String
        If String.IsNullOrEmpty(propertyName) Then Throw New ArgumentNullException(NameOf(propertyName))

        ' 1) 优先依赖属性：遍历元素类型的 DependencyProperty 字段（公共静态），找注册名匹配的。
        Dim dpValue As Object = TryReadDependencyProperty(element, propertyName)
        If dpValue IsNot Nothing Then
            Return ScriptResultSerializer.Serialize(dpValue)
        End If

        ' 2) CLR 属性。
        Dim pi As PropertyInfo = element.GetType().GetProperty(propertyName,
            BindingFlags.Public Or BindingFlags.Instance)
        If pi Is Nothing OrElse Not pi.CanRead OrElse pi.GetIndexParameters().Length > 0 Then
            Throw New InvalidOperationException(
                $"元素 {element.GetType().Name} 上找不到可读属性 {propertyName}。")
        End If
        Dim value As Object = pi.GetValue(element)
        Return ScriptResultSerializer.Serialize(value)
    End Function

    ' TryRead 依赖属性：找不到或值为 null/默认返回 Nothing（让调用方退化到 CLR 属性）。
    ' 注意：依赖属性的默认值（如 DependencyProperty.UnsetValue）也视为「未读到」。
    ' 精确匹配字段名 == propertyName & "Property"（避免 Width 误命中 ActualWidthProperty）。
    Private Shared Function TryReadDependencyProperty(element As Global.System.Windows.DependencyObject, propertyName As String) As Object
        Try
            Dim depObj As Global.System.Windows.DependencyObject = element
            Dim targetFieldName As String = propertyName & "Property"
            For Each fld As FieldInfo In element.GetType().GetFields(
                BindingFlags.Public Or BindingFlags.Static Or BindingFlags.FlattenHierarchy)
                If Not GetType(Global.System.Windows.DependencyProperty).IsAssignableFrom(fld.FieldType) Then Continue For
                If String.Equals(fld.Name, targetFieldName, StringComparison.Ordinal) Then
                    Dim dp As Global.System.Windows.DependencyProperty =
                        DirectCast(fld.GetValue(Nothing), Global.System.Windows.DependencyProperty)
                    Dim v As Object = depObj.GetValue(dp)
                    If v Is Nothing Then Return Nothing
                    ' UnsetValue / 默认值视为未读到（让 CLR 兜底）。
                    If v Is Global.System.Windows.DependencyProperty.UnsetValue Then Return Nothing
                    Return v
                End If
            Next
            Return Nothing
        Catch
            Return Nothing
        End Try
    End Function

    Private Shared Sub WriteProperty(element As Global.System.Windows.DependencyObject, propertyName As String, value As String)
        If String.IsNullOrEmpty(propertyName) Then Throw New ArgumentNullException(NameOf(propertyName))

        ' 1) 依赖属性优先（用 SetValue）。
        If TryWriteDependencyProperty(element, propertyName, value) Then Return

        ' 2) CLR 属性。
        Dim pi As PropertyInfo = element.GetType().GetProperty(propertyName,
            BindingFlags.Public Or BindingFlags.Instance)
        If pi Is Nothing OrElse Not pi.CanWrite OrElse pi.GetIndexParameters().Length > 0 Then
            Throw New InvalidOperationException(
                $"元素 {element.GetType().Name} 上找不到可写属性 {propertyName}。")
        End If
        Dim converted As Object = ConvertValueToType(pi.PropertyType, value)
        pi.SetValue(element, converted)
    End Sub

    ' 尝试用 SetValue 写依赖属性。命中返回 True，否则 False。
    ' 精确匹配字段名 == propertyName & "Property"（避免 Width 误命中 ActualWidthProperty）。
    Private Shared Function TryWriteDependencyProperty(element As Global.System.Windows.DependencyObject,
                                                       propertyName As String, value As String) As Boolean
        Try
            Dim depObj As Global.System.Windows.DependencyObject = element
            Dim targetFieldName As String = propertyName & "Property"
            For Each fld As FieldInfo In element.GetType().GetFields(
                BindingFlags.Public Or BindingFlags.Static Or BindingFlags.FlattenHierarchy)
                If Not GetType(Global.System.Windows.DependencyProperty).IsAssignableFrom(fld.FieldType) Then Continue For
                If String.Equals(fld.Name, targetFieldName, StringComparison.Ordinal) Then
                    Dim dp As Global.System.Windows.DependencyProperty =
                        DirectCast(fld.GetValue(Nothing), Global.System.Windows.DependencyProperty)
                    ' 读当前值推断类型（依赖属性无公开元数据直查属性类型，靠现有值兜底）。
                    Dim current As Object = depObj.GetValue(dp)
                    Dim targetType As Type = If(current?.GetType(), GetType(String))
                    depObj.SetValue(dp, ConvertValueToType(targetType, value))
                    Return True
                End If
            Next
            Return False
        Catch
            Return False
        End Try
    End Function

    ' 把字符串 value 转成属性类型。兼容失败抛 ArgumentException。
    Friend Shared Function ConvertValueToType(targetType As Type, value As String) As Object
        If targetType Is GetType(String) Then Return value
        Try
            Dim converter As ComponentModel.TypeConverter = ComponentModel.TypeDescriptor.GetConverter(targetType)
            If converter IsNot Nothing AndAlso converter.CanConvertFrom(GetType(String)) Then
                Return converter.ConvertFromString(Nothing, Globalization.CultureInfo.InvariantCulture, value)
            End If
        Catch
        End Try
        ' 退化：直接赋字符串，由运行时自己抛（如果不兼容）。
        If targetType.IsAssignableFrom(GetType(String)) Then Return value
        Throw New ArgumentException($"无法把字符串 ""{value}"" 转换为属性类型 {targetType.Name}。")
    End Function
End Class

''' <summary>
''' 软超时信号异常：脚本宿主方法检测到超时时抛出，由 ScriptEngine 捕获并转成超时错误。
''' </summary>
Public Class ScriptTimeoutException
    Inherits InvalidOperationException

    Public Sub New()
        MyBase.New("脚本执行超时（由宿主方法检查触发）。")
    End Sub
End Class
