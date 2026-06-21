Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServerForDevEnv.WpfDebugging.Target

' ElementInteractor 的纯逻辑测试：
'   - ResolveFillStrategy：控件类型名 → fill 设置策略。纯字符串映射，不碰任何 WPF 对象/线程。
' 真实 click/fill 的 UI 操作（AutomationPeer.Invoke / RaiseEvent / 设控件属性）涉及真 UI 线程，
' 标注留集成验证 #24。
' 本测试类零副作用：不连 pipe、不写文件、不启进程、不碰真 UI 线程。

<TestClass>
Public Class ElementInteractorTests

    ' ===== ResolveFillStrategy 各分支 =====

    <TestMethod>
    Public Sub ResolveFillStrategy_TextBox_Goes_Text_Property()
        Assert.AreEqual(FillStrategy.Text, ElementInteractor.ResolveFillStrategy("TextBox"))
    End Sub

    <TestMethod>
    Public Sub ResolveFillStrategy_PasswordBox_Goes_Password_Property()
        Assert.AreEqual(FillStrategy.Password, ElementInteractor.ResolveFillStrategy("PasswordBox"))
    End Sub

    <TestMethod>
    Public Sub ResolveFillStrategy_ComboBox_Goes_SelectedValue()
        Assert.AreEqual(FillStrategy.SelectedValue, ElementInteractor.ResolveFillStrategy("ComboBox"))
    End Sub

    <TestMethod>
    Public Sub ResolveFillStrategy_CheckBox_Goes_IsChecked()
        Assert.AreEqual(FillStrategy.IsChecked, ElementInteractor.ResolveFillStrategy("CheckBox"))
    End Sub

    <TestMethod>
    Public Sub ResolveFillStrategy_RadioButton_Goes_IsChecked()
        Assert.AreEqual(FillStrategy.IsChecked, ElementInteractor.ResolveFillStrategy("RadioButton"))
    End Sub

    <TestMethod>
    Public Sub ResolveFillStrategy_ToggleButton_Goes_IsChecked()
        Assert.AreEqual(FillStrategy.IsChecked, ElementInteractor.ResolveFillStrategy("ToggleButton"))
    End Sub

    <TestMethod>
    Public Sub ResolveFillStrategy_Slider_Goes_RangeValue()
        Assert.AreEqual(FillStrategy.RangeValue, ElementInteractor.ResolveFillStrategy("Slider"))
    End Sub

    <TestMethod>
    Public Sub ResolveFillStrategy_ProgressBar_Goes_RangeValue()
        Assert.AreEqual(FillStrategy.RangeValue, ElementInteractor.ResolveFillStrategy("ProgressBar"))
    End Sub

    <TestMethod>
    Public Sub ResolveFillStrategy_ScrollBar_Goes_RangeValue()
        ' ScrollBar 也是 RangeBase 派生，命中 RangeValue 分支。
        Assert.AreEqual(FillStrategy.RangeValue, ElementInteractor.ResolveFillStrategy("ScrollBar"))
    End Sub

    <TestMethod>
    Public Sub ResolveFillStrategy_UnknownType_FallsBack_To_Reflection()
        ' 未知/装饰性控件退化反射 Text/Content。
        Assert.AreEqual(FillStrategy.ReflectFallback, ElementInteractor.ResolveFillStrategy("TextBlock"))
        Assert.AreEqual(FillStrategy.ReflectFallback, ElementInteractor.ResolveFillStrategy("Label"))
        Assert.AreEqual(FillStrategy.ReflectFallback, ElementInteractor.ResolveFillStrategy("Border"))
        Assert.AreEqual(FillStrategy.ReflectFallback, ElementInteractor.ResolveFillStrategy("MyCustomControl"))
    End Sub

    <TestMethod>
    Public Sub ResolveFillStrategy_IsCaseSensitive()
        ' WPF 类型名首字母大写；小写不命中分流表，走退化分支。
        Assert.AreEqual(FillStrategy.ReflectFallback, ElementInteractor.ResolveFillStrategy("textbox"))
        Assert.AreEqual(FillStrategy.ReflectFallback, ElementInteractor.ResolveFillStrategy("BUTTON"))
    End Sub

    <TestMethod>
    Public Sub ResolveFillStrategy_NullOrEmpty_Goes_Fallback()
        Assert.AreEqual(FillStrategy.ReflectFallback, ElementInteractor.ResolveFillStrategy(Nothing))
        Assert.AreEqual(FillStrategy.ReflectFallback, ElementInteractor.ResolveFillStrategy(""))
    End Sub

    <TestMethod>
    Public Sub ResolveFillStrategy_AutoCompleteBox_NotInKnownSet_FallsBack()
        ' 确认自定义控件（如 AutoCompleteBox，不在分流白名单）走退化路径，不被误判。
        Assert.AreEqual(FillStrategy.ReflectFallback, ElementInteractor.ResolveFillStrategy("AutoCompleteBox"))
    End Sub

End Class
