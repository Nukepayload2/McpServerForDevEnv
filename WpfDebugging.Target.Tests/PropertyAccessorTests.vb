Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServerForDevEnv.WpfDebugging.Target

' PropertyAccessor 的读写测试。
'
' 这些用例会真的构造 WPF 控件（Button / Image），所以必须跑在 STA 线程上——
' WPF 对象有线程亲和性，MTA 线程上构造 UIElement 会抛 "calling thread must be STA"。
' MSTest 的 <STATestMethod> 会为每个用例开 STA 线程。

<TestClass>
Public Class PropertyAccessorTests

    ' ===== 读 =====

    <STATestMethod>
    Public Sub GetPropertyValue_Reads_Double_Without_BinaryNoise()
        Dim b As New Global.System.Windows.Controls.Button()
        b.Width = 645.6R

        ' 不能返回 645.5999999999999 这种二进制浮点残留
        WithCulture("en-US",
            Sub() Assert.AreEqual("645.6", PropertyAccessor.GetPropertyValue(b, "Width")))
    End Sub

    <STATestMethod>
    Public Sub GetPropertyValue_UsesCurrentUiCulture()
        ' 界面按用户区域性呈现数值，读取必须与其一致
        Dim b As New Global.System.Windows.Controls.Button()
        b.Width = 645.6R

        WithCulture("de-DE",
            Sub() Assert.AreEqual("645,6", PropertyAccessor.GetPropertyValue(b, "Width")))
    End Sub

    <STATestMethod>
    Public Sub SetPropertyValue_ParsesUsingCurrentUiCulture()
        Dim b As New Global.System.Windows.Controls.Button()

        WithCulture("de-DE",
            Sub() PropertyAccessor.SetPropertyValue(b, "Width", "12,5"))

        Assert.AreEqual(12.5R, b.Width, "de-DE 下小数点应为逗号")
    End Sub

    ''' <summary>在指定 UI 区域性下执行断言，结束后恢复原值。</summary>
    Private Shared Sub WithCulture(name As String, action As Action)
        Dim saved As Globalization.CultureInfo = Globalization.CultureInfo.CurrentUICulture
        Try
            Globalization.CultureInfo.CurrentUICulture = New Globalization.CultureInfo(name)
            action()
        Finally
            Globalization.CultureInfo.CurrentUICulture = saved
        End Try
    End Sub

    <STATestMethod>
    Public Sub GetPropertyValue_Enum_ReturnsName()
        Dim b As New Global.System.Windows.Controls.Button()
        b.Visibility = Global.System.Windows.Visibility.Collapsed

        Assert.AreEqual("Collapsed", PropertyAccessor.GetPropertyValue(b, "Visibility"))
    End Sub

    <STATestMethod>
    Public Sub GetPropertyValue_ImageSource_IncludesDimensions()
        ' UI 预览验证的关键读数：Source 必须能报出位图尺寸
        Dim bmp As New Global.System.Windows.Media.Imaging.WriteableBitmap(
            8, 4, 96, 96, Global.System.Windows.Media.PixelFormats.Bgra32, Nothing)

        Dim shown As String = PropertyAccessor.FormatValue(bmp)
        StringAssert.Contains(shown, "8x4")
    End Sub

    <STATestMethod>
    Public Sub GetPropertyValue_UnknownProperty_ThrowsWithCandidates()
        Dim b As New Global.System.Windows.Controls.Button()

        Try
            PropertyAccessor.GetPropertyValue(b, "NoSuchProperty")
            Assert.Fail("属性不存在时应抛异常")
        Catch ex As InvalidOperationException
            ' 报错必须给出候选，否则调用方只能瞎猜
            StringAssert.Contains(ex.Message, "Width")
            StringAssert.Contains(ex.Message, "NoSuchProperty")
        End Try
    End Sub

    <STATestMethod>
    Public Sub GetPropertyValue_EmptyName_Throws()
        Dim b As New Global.System.Windows.Controls.Button()
        Try
            PropertyAccessor.GetPropertyValue(b, "  ")
            Assert.Fail("空属性名应抛 ArgumentException")
        Catch ex As ArgumentException
            StringAssert.Contains(ex.Message, "属性名不能为空")
        End Try
    End Sub

    ' ===== 写 =====

    <STATestMethod>
    Public Sub SetPropertyValue_Converts_Thickness_AndReadsBack()
        Dim b As New Global.System.Windows.Controls.Button()
        PropertyAccessor.SetPropertyValue(b, "Margin", "1,2,3,4")

        Assert.AreEqual(New Global.System.Windows.Thickness(1, 2, 3, 4), b.Margin)
    End Sub

    <STATestMethod>
    Public Sub SetPropertyValue_Converts_Enum()
        Dim b As New Global.System.Windows.Controls.Button()
        PropertyAccessor.SetPropertyValue(b, "Visibility", "Collapsed")

        Assert.AreEqual(Global.System.Windows.Visibility.Collapsed, b.Visibility)
    End Sub

    <STATestMethod>
    Public Sub SetPropertyValue_Converts_Boolean()
        Dim cb As New Global.System.Windows.Controls.CheckBox()
        PropertyAccessor.SetPropertyValue(cb, "IsChecked", "true")

        Assert.AreEqual(True, cb.IsChecked)
    End Sub

    <STATestMethod>
    Public Sub SetPropertyValue_ReadOnlyDependencyProperty_Throws()
        Dim b As New Global.System.Windows.Controls.Button()

        Try
            PropertyAccessor.SetPropertyValue(b, "ActualWidth", "100")
            Assert.Fail("只读依赖属性应拒绝写入")
        Catch ex As InvalidOperationException
            StringAssert.Contains(ex.Message, "只读")
        End Try
    End Sub

    <STATestMethod>
    Public Sub SetPropertyValue_NullLiteral_ClearsReferenceProperty()
        Dim b As New Global.System.Windows.Controls.Button()
        b.Tag = "something"

        PropertyAccessor.SetPropertyValue(b, "Tag", "null")
        Assert.IsNull(b.Tag)
    End Sub

    <STATestMethod>
    Public Sub SetPropertyValue_UnknownProperty_Throws()
        Dim b As New Global.System.Windows.Controls.Button()

        Try
            PropertyAccessor.SetPropertyValue(b, "NoSuchProperty", "1")
            Assert.Fail("属性不存在时应抛异常")
        Catch ex As InvalidOperationException
            StringAssert.Contains(ex.Message, "NoSuchProperty")
        End Try
    End Sub

    ' ===== 转换 =====

    <TestMethod>
    Public Sub ConvertTo_Boolean_Accepts_Zero_And_One()
        Assert.AreEqual(True, PropertyAccessor.ConvertTo(GetType(Boolean), "1", "P"))
        Assert.AreEqual(False, PropertyAccessor.ConvertTo(GetType(Boolean), "0", "P"))
    End Sub

    <TestMethod>
    Public Sub ConvertTo_NullLiteral_ForNonNullableValueType_Throws()
        Try
            PropertyAccessor.ConvertTo(GetType(Integer), "null", "P")
            Assert.Fail("non-nullable 值类型不应接受 null")
        Catch ex As InvalidOperationException
            StringAssert.Contains(ex.Message, "不接受 null")
        End Try
    End Sub

    <TestMethod>
    Public Sub ConvertTo_NullableInt_FromString()
        Dim v As Object = PropertyAccessor.ConvertTo(GetType(Nullable(Of Integer)), "42", "P")
        Assert.AreEqual(42, CInt(v))
    End Sub

    <TestMethod>
    Public Sub ConvertTo_UnparsableValue_ThrowsWithPropertyName()
        Try
            PropertyAccessor.ConvertTo(GetType(Integer), "abc", "MyProp")
            Assert.Fail("无法转换时应抛异常")
        Catch ex As InvalidOperationException
            StringAssert.Contains(ex.Message, "MyProp")
        End Try
    End Sub
End Class
