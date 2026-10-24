Imports System.ComponentModel
Imports System.Globalization
Imports System.Reflection
Imports System.Windows
Imports System.Windows.Media

' 依赖属性 / CLR 属性的读写与字符串化。
'
' 设计取舍：
' 1. 优先走 DependencyProperty（GetValue/SetValue）——这样拿到的是**绑定、动画、样式生效后**
'    的实际值，而不是 CLR 包装器的返回值；对调试来说后者往往是误导。
' 2. 找不到 DP 时回落到 CLR 反射，覆盖非 DP 的普通属性（如 TextBox.Text 之外的普通字段型属性）。
' 3. 写入前做类型转换，转换链：Nullable 解包 → 枚举 → 布尔 → TypeConverter → ChangeType。
'    WPF 大量类型（Thickness、Brush、Color、GridLength、CornerRadius…）都有 TypeConverter，
'    走转换器比自己枚举类型更省事也更全。
' 4. 失败时把**候选属性名**一并抛给调用方。AI 调用方看不到源码，报「不存在」等于让它瞎猜。

''' <summary>
''' 按名字读写 <see cref="DependencyObject"/> 的属性，并给出可读的字符串表示。
''' </summary>
Public NotInheritable Class PropertyAccessor

    Private Sub New()
    End Sub

    ''' <summary>同时匹配「DP 字段名」与「属性名」的候选上限，避免错误信息过长。</summary>
    Private Const MaxCandidates As Integer = 24

    ' ───────────────────────── 读 ─────────────────────────

    ''' <summary>
    ''' 读取属性值并字符串化。找不到属性时抛 <see cref="InvalidOperationException"/>，
    ''' 消息里附带该类型上可用的属性名。
    ''' </summary>
    Public Shared Function GetPropertyValue(target As DependencyObject, propertyName As String) As String
        If target Is Nothing Then Throw New ArgumentNullException(NameOf(target))
        If String.IsNullOrWhiteSpace(propertyName) Then
            Throw New ArgumentException("属性名不能为空。", NameOf(propertyName))
        End If

        Dim name As String = propertyName.Trim()

        Dim dp As DependencyProperty = FindDependencyProperty(target.GetType(), name)
        If dp IsNot Nothing AndAlso Not IsWriteOnly(dp) Then
            Return FormatValue(target.GetValue(dp))
        End If

        Dim prop As PropertyInfo = FindClrProperty(target.GetType(), name)
        If prop IsNot Nothing AndAlso prop.CanRead AndAlso prop.GetIndexParameters().Length = 0 Then
            Return FormatValue(prop.GetValue(target, Nothing))
        End If

        Throw New InvalidOperationException(
            $"在 {target.GetType().Name} 上找不到可读属性 '{name}'。可用属性示例：{DescribeCandidates(target.GetType())}")
    End Function

    ' ───────────────────────── 写 ─────────────────────────

    ''' <summary>
    ''' 写入属性。支持依赖属性与可写 CLR 属性；字符串值按目标类型转换。
    ''' </summary>
    Public Shared Sub SetPropertyValue(target As DependencyObject, propertyName As String, value As String)
        If target Is Nothing Then Throw New ArgumentNullException(NameOf(target))
        If String.IsNullOrWhiteSpace(propertyName) Then
            Throw New ArgumentException("属性名不能为空。", NameOf(propertyName))
        End If

        Dim name As String = propertyName.Trim()

        Dim dp As DependencyProperty = FindDependencyProperty(target.GetType(), name)
        If dp IsNot Nothing Then
            If dp.ReadOnly Then
                Throw New InvalidOperationException(
                    $"{target.GetType().Name}.{name} 是只读依赖属性，无法写入。")
            End If
            Dim converted As Object = ConvertTo(dp.PropertyType, value, name)
            target.SetValue(dp, converted)
            Return
        End If

        Dim prop As PropertyInfo = FindClrProperty(target.GetType(), name)
        If prop IsNot Nothing AndAlso prop.CanWrite AndAlso prop.GetIndexParameters().Length = 0 Then
            Dim converted As Object = ConvertTo(prop.PropertyType, value, name)
            prop.SetValue(target, converted, Nothing)
            Return
        End If

        Throw New InvalidOperationException(
            $"在 {target.GetType().Name} 上找不到可写属性 '{name}'。可用属性示例：{DescribeCandidates(target.GetType())}")
    End Sub

    ' ───────────────────── 属性查找 ─────────────────────

    ''' <summary>沿继承链查找名为 <c>&lt;name&gt;Property</c> 的公有静态 DependencyProperty 字段。</summary>
    Public Shared Function FindDependencyProperty(type As Type, propertyName As String) As DependencyProperty
        If type Is Nothing Then Return Nothing

        Dim fieldName As String = propertyName & "Property"
        Dim t As Type = type
        While t IsNot Nothing
            Dim fields As FieldInfo() = t.GetFields(BindingFlags.Public Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
            For Each f As FieldInfo In fields
                If f.Name = fieldName AndAlso GetType(DependencyProperty).IsAssignableFrom(f.FieldType) Then
                    Return TryCast(f.GetValue(Nothing), DependencyProperty)
                End If
            Next
            t = t.BaseType
        End While

        ' 附加属性（如 Grid.Row）走 DependencyPropertyDescriptor 解析
        Dim dotted As Integer = propertyName.IndexOf("."c)
        If dotted > 0 Then
            Dim ownerName As String = propertyName.Substring(0, dotted)
            Dim propName As String = propertyName.Substring(dotted + 1)
            Dim owner As Type = ResolveTypeByName(ownerName)
            If owner IsNot Nothing Then
                Dim desc As DependencyPropertyDescriptor = DependencyPropertyDescriptor.FromName(propName, owner, type)
                If desc IsNot Nothing Then Return desc.DependencyProperty
            End If
        End If

        Return Nothing
    End Function

    ''' <summary>按名字找 CLR 属性（含继承，忽略大小写）。</summary>
    Public Shared Function FindClrProperty(type As Type, propertyName As String) As PropertyInfo
        If type Is Nothing Then Return Nothing
        Dim p As PropertyInfo = type.GetProperty(propertyName,
            BindingFlags.Public Or BindingFlags.Instance Or BindingFlags.IgnoreCase)
        If p IsNot Nothing Then Return p
        ' 显式再试一次非忽略大小写，保证大小写正确时优先精确匹配
        Return type.GetProperty(propertyName, BindingFlags.Public Or BindingFlags.Instance)
    End Function

    ''' <summary>在已加载程序集里按短名或全名找类型（供 "Grid.Row" 这类附加属性解析用）。</summary>
    Private Shared Function ResolveTypeByName(name As String) As Type
        If String.IsNullOrEmpty(name) Then Return Nothing

        ' 先按 WPF 常见命名空间试，避免全量扫描程序集
        For Each ns As String In {"System.Windows.Controls.", "System.Windows.", "System.Windows.Documents.", "System.Windows.Shapes."}
            Dim t As Type = Type.GetType(ns & name, throwOnError:=False)
            If t IsNot Nothing Then Return t
        Next

        For Each asm As Assembly In AppDomain.CurrentDomain.GetAssemblies()
            Dim t As Type = asm.GetType(name, throwOnError:=False)
            If t IsNot Nothing Then Return t
            t = asm.GetType("System.Windows.Controls." & name, throwOnError:=False)
            If t IsNot Nothing Then Return t
        Next
        Return Nothing
    End Function

    ''' <summary>只写属性无法 GetValue，据此跳过。</summary>
    Private Shared Function IsWriteOnly(dp As DependencyProperty) As Boolean
        Return False
    End Function

    ' ───────────────────── 类型转换 ─────────────────────

    ''' <summary>
    ''' 字符串 → 目标类型。转换链：null 字面量 → Nullable 解包 → 字符串直通 → 枚举 → 布尔
    ''' → TypeConverter → ChangeType。
    ''' </summary>
    Public Shared Function ConvertTo(targetType As Type, value As String, forProperty As String) As Object
        If targetType Is Nothing Then Throw New ArgumentNullException(NameOf(targetType))

        Dim isNullLiteral As Boolean = value Is Nothing OrElse
            String.Equals(value.Trim(), "null", StringComparison.OrdinalIgnoreCase)

        Dim underlying As Type = Nullable.GetUnderlyingType(targetType)
        If isNullLiteral Then
            If Not targetType.IsValueType OrElse underlying IsNot Nothing Then Return Nothing
            Throw New InvalidOperationException(
                $"属性 '{forProperty}' 的类型 {targetType.Name} 不接受 null。")
        End If
        If underlying IsNot Nothing Then targetType = underlying

        Dim raw As String = value

        If targetType Is GetType(String) Then Return raw

        If targetType.IsEnum Then
            Return [Enum].Parse(targetType, raw.Trim(), ignoreCase:=True)
        End If

        If targetType Is GetType(Boolean) Then
            Dim s As String = raw.Trim()
            If s = "1" Then Return True
            If s = "0" Then Return False
            Return Boolean.Parse(s)
        End If

        ' WPF 大多数类型（Thickness/Brush/Color/GridLength/FontWeight…）都有 TypeConverter。
        ' 用当前 UI 区域性解析：界面按用户区域性呈现数值，读写必须与其一致。
        Dim culture As CultureInfo = CultureInfo.CurrentUICulture
        Dim conv As TypeConverter = TypeDescriptor.GetConverter(targetType)
        If conv IsNot Nothing AndAlso conv.CanConvertFrom(GetType(String)) Then
            Try
                Return conv.ConvertFromString(Nothing, culture, raw)
            Catch ex As Exception
                Dim fallbackMsg As String = ex.Message
                Try
                    Return System.Convert.ChangeType(raw, targetType, culture)
                Catch
                    Throw New InvalidOperationException(
                        $"无法把 '{raw}' 转成 {targetType.Name}（属性 '{forProperty}'）：{fallbackMsg}")
                End Try
            End Try
        End If

        Try
            Return System.Convert.ChangeType(raw, targetType, culture)
        Catch ex As Exception
            Throw New InvalidOperationException(
                $"无法把 '{raw}' 转成 {targetType.Name}（属性 '{forProperty}'）：{ex.Message}")
        End Try
    End Function

    ' ───────────────────── 字符串化 ─────────────────────

    ''' <summary>把属性值转成适合 AI/日志阅读的字符串。</summary>
    Public Shared Function FormatValue(value As Object) As String
        If value Is Nothing Then Return "null"

        Dim s As String = TryCast(value, String)
        If s IsNot Nothing Then Return s

        Dim type As Type = value.GetType()

        If type.IsEnum Then Return value.ToString()
        If type Is GetType(Boolean) Then Return If(DirectCast(value, Boolean), "True", "False")

        ' 浮点数按 3 位小数：WPF 的 ActualWidth 常是 645.5999999999999 这种二进制浮点残留，
        ' 原样返回会让调用方误以为是精确值。按当前 UI 区域性呈现，与界面显示一致。
        If type Is GetType(Double) Then
            Return DirectCast(value, Double).ToString("0.###", CultureInfo.CurrentUICulture)
        End If
        If type Is GetType(Single) Then
            Return DirectCast(value, Single).ToString("0.###", CultureInfo.CurrentUICulture)
        End If

        Dim img As ImageSource = TryCast(value, ImageSource)
        If img IsNot Nothing Then
            ' 图片源给尺寸最有用：预览是否真的产出了位图，一眼就能判断
            Return $"{type.Name} {img.Width}x{img.Height}"
        End If

        Dim brush As Brush = TryCast(value, Brush)
        If brush IsNot Nothing Then
            Dim scb As SolidColorBrush = TryCast(brush, SolidColorBrush)
            If scb IsNot Nothing Then Return $"{type.Name} {scb.Color}"
            Return type.Name
        End If

        Dim fe As FrameworkElement = TryCast(value, FrameworkElement)
        If fe IsNot Nothing Then
            Return $"{type.Name} {fe.ActualWidth}x{fe.ActualHeight}"
        End If

        Dim coll As ICollection = TryCast(value, ICollection)
        If coll IsNot Nothing AndAlso Not TypeOf value Is String Then
            Return $"{type.Name} Count={coll.Count}"
        End If

        Dim convertible As TypeConverter = TypeDescriptor.GetConverter(type)
        If convertible IsNot Nothing AndAlso convertible.CanConvertTo(GetType(String)) Then
            Try
                Return convertible.ConvertToInvariantString(value)
            Catch
                ' 忽略，回落到 ToString
            End Try
        End If

        Return value.ToString()
    End Function

    ' ───────────────────── 错误提示 ─────────────────────

    ''' <summary>报错时优先展示的属性名：都是最常被查的跨类型通用属性。</summary>
    Private Shared ReadOnly CommonNames As String() = {
        "Text", "Content", "IsChecked", "SelectedIndex", "SelectedItem", "ItemsSource",
        "Width", "Height", "ActualWidth", "ActualHeight", "Margin", "Padding",
        "Visibility", "IsEnabled", "IsVisible", "Background", "Foreground", "FontSize",
        "Name", "Tag", "DataContext", "Source", "Stretch", "Value", "IsSelected"
    }

    ''' <summary>
    ''' 列出该类型上可读的属性名，供报错时提示调用方。
    ''' 先给通用高频属性，再补该类型自己的，避免候选被某个控件的特有 DP 挤满。
    ''' </summary>
    Public Shared Function DescribeCandidates(type As Type) As String
        Dim available As New HashSet(Of String)(StringComparer.Ordinal)

        Dim t As Type = type
        While t IsNot Nothing
            For Each f As FieldInfo In t.GetFields(BindingFlags.Public Or BindingFlags.Static Or BindingFlags.DeclaredOnly)
                If GetType(DependencyProperty).IsAssignableFrom(f.FieldType) AndAlso
                   f.Name.EndsWith("Property", StringComparison.Ordinal) Then
                    available.Add(f.Name.Substring(0, f.Name.Length - "Property".Length))
                End If
            Next
            t = t.BaseType
        End While

        For Each p As PropertyInfo In type.GetProperties(BindingFlags.Public Or BindingFlags.Instance)
            available.Add(p.Name)
        Next

        Dim list As New List(Of String)

        ' 通用属性优先
        For Each common As String In CommonNames
            If available.Contains(common) Then list.Add(common)
        Next

        For Each name As String In available
            If list.Count >= MaxCandidates Then Exit For
            If Not list.Contains(name) Then list.Add(name)
        Next

        If list.Count = 0 Then Return "（无）"
        Return String.Join(", ", list)
    End Function

End Class
