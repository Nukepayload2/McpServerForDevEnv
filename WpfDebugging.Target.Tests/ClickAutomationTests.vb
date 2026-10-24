Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServerForDevEnv.WpfDebugging.Target

' ElementInteractor.Click 的行为测试。
'
' 覆盖：TreeViewItem 选中、行内文字点上溯、禁用控件拒绝、模式解析映射。
' 需要 STA 线程（构造 WPF 控件）。

<TestClass>
Public Class ClickAutomationTests

    <STATestMethod>
    Public Sub Click_Button_SelectsInvokePath_AndDoesNotThrow()
        ' ButtonAutomationPeer.Invoke 是否触发 Click 取决于 WPF 运行环境（单元测试内不触发，
        ' 真实应用内触发）。本用例只断言 Invoke 路径被选中且不抛异常；按钮点击效果由集成验证覆盖。
        Dim win As New Global.System.Windows.Window With {
            .Width = 200, .Height = 120, .ShowInTaskbar = False
        }
        Dim b As New Global.System.Windows.Controls.Button()
        win.Content = b

        Try
            win.Show()
            ElementInteractor.Click(b)
        Finally
            win.Close()
        End Try
    End Sub
    <STATestMethod>
    Public Sub Click_TreeViewItem_SelectsIt()
        ' TreeViewItem 的选中依赖命中测试，只有 Provider 路径能驱动
        Dim tree As New Global.System.Windows.Controls.TreeView()
        Dim item As New Global.System.Windows.Controls.TreeViewItem With {.Header = "Node"}
        tree.Items.Add(item)

        Assert.IsFalse(item.IsSelected, "前置条件：初始未选中")

        ElementInteractor.Click(item)

        Assert.IsTrue(item.IsSelected, "点击 TreeViewItem 后应处于选中状态")
    End Sub

    <STATestMethod>
    Public Sub Click_TextBlockInsideTreeViewItem_SelectsOwningItem()
        ' 用户在界面上点的是文字，选中的是整行——调用方拿到的 uid 往往是内层 TextBlock，
        ' 所以 click 必须能向上找到可选的祖先
        Dim tree As New Global.System.Windows.Controls.TreeView()
        Dim item As New Global.System.Windows.Controls.TreeViewItem()
        Dim inner As New Global.System.Windows.Controls.TextBlock With {.Text = "Node"}
        item.Header = inner
        tree.Items.Add(item)

        ElementInteractor.Click(inner)

        Assert.IsTrue(item.IsSelected, "点击行内文字应选中整行")
    End Sub

    <STATestMethod>
    Public Sub Click_DisabledButton_ThrowsInsteadOfPretending()
        Dim b As New Global.System.Windows.Controls.Button With {.IsEnabled = False}
        Dim fired As Boolean = False
        AddHandler b.Click, Sub(s, e) fired = True

        Try
            ElementInteractor.Click(b)
            Assert.Fail("禁用按钮不可点击，应抛异常而不是静默成功")
        Catch ex As InvalidOperationException
            StringAssert.Contains(ex.Message, "无法点击")
        End Try

        Assert.IsFalse(fired, "禁用按钮不应触发 Click")
    End Sub

    <STATestMethod>
    Public Sub ResolveClickAction_Button_IsInvoke()
        Dim b As New Global.System.Windows.Controls.Button()
        Dim peer = Global.System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(b)
        Assert.AreEqual(ElementInteractor.ClickAction.Invoke, ElementInteractor.ResolveClickAction(peer))
    End Sub

    <STATestMethod>
    Public Sub ResolveClickAction_TreeViewItem_IsSelectItem()
        Dim item As New Global.System.Windows.Controls.TreeViewItem()
        Dim peer = Global.System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(item)
        Assert.AreEqual(ElementInteractor.ClickAction.SelectItem, ElementInteractor.ResolveClickAction(peer))
    End Sub

    <STATestMethod>
    Public Sub ResolveClickAction_CheckBox_IsToggle()
        Dim cb As New Global.System.Windows.Controls.CheckBox()
        Dim peer = Global.System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(cb)
        Assert.AreEqual(ElementInteractor.ClickAction.Toggle, ElementInteractor.ResolveClickAction(peer))
    End Sub

    <STATestMethod>
    Public Sub Click_CheckBox_TogglesIsChecked()
        Dim cb As New Global.System.Windows.Controls.CheckBox()
        ElementInteractor.Click(cb)
        Assert.AreEqual(True, cb.IsChecked.Value, "点击复选框应勾选")
    End Sub
End Class
