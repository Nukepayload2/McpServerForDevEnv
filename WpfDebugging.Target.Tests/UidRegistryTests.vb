Imports System.Globalization
Imports System.Windows
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServerForDevEnv.WpfDebugging.Target

' UidRegistry 的纯逻辑测试。实例化 DependencyObject 不启动 Dispatcher / 不创建窗口 /
' 不连 pipe / 不写文件，属纯内存对象操作，无副作用。

<TestClass>
Public Class UidRegistryTests

    <TestMethod>
    Public Sub GetOrCreateUid_SameElement_ReturnsSameUid()
        Dim reg As New UidRegistry()
        Dim el As New DependencyObject()
        Dim uid1 As String = reg.GetOrCreateUid(el)
        Dim uid2 As String = reg.GetOrCreateUid(el)

        Assert.IsFalse(String.IsNullOrEmpty(uid1))
        Assert.AreEqual(uid1, uid2)
    End Sub

    <TestMethod>
    Public Sub GetOrCreateUid_DifferentElements_ReturnDifferentUids()
        Dim reg As New UidRegistry()
        Dim a As New DependencyObject()
        Dim b As New DependencyObject()
        Dim uidA As String = reg.GetOrCreateUid(a)
        Dim uidB As String = reg.GetOrCreateUid(b)

        Assert.AreNotEqual(uidA, uidB)
    End Sub

    <TestMethod>
    Public Sub Resolve_KnownUid_ReturnsElement()
        Dim reg As New UidRegistry()
        Dim el As New DependencyObject()
        Dim uid As String = reg.GetOrCreateUid(el)

        Dim back As DependencyObject = reg.Resolve(uid)
        Assert.IsNotNull(back)
        Assert.AreSame(el, back)
    End Sub

    <TestMethod>
    Public Sub Resolve_UnknownUid_ReturnsNull()
        Dim reg As New UidRegistry()
        Assert.IsNull(reg.Resolve("999999999"))
    End Sub

    <TestMethod>
    Public Sub Resolve_InvalidUid_ReturnsNull()
        Dim reg As New UidRegistry()
        Assert.IsNull(reg.Resolve("not-a-number"))
        Assert.IsNull(reg.Resolve(""))
        Assert.IsNull(reg.Resolve(Nothing))
    End Sub

    <TestMethod>
    Public Sub Uid_Matches_HashCode_String_Form()
        Dim reg As New UidRegistry()
        Dim el As New DependencyObject()
        Dim uid As String = reg.GetOrCreateUid(el)

        Assert.AreEqual(el.GetHashCode().ToString(CultureInfo.InvariantCulture), uid)
    End Sub

    <TestMethod>
    Public Sub Clear_DropsAllEntries()
        Dim reg As New UidRegistry()
        Dim el As New DependencyObject()
        Dim uid As String = reg.GetOrCreateUid(el)
        Assert.AreEqual(1, reg.Count)

        reg.Clear()
        Assert.AreEqual(0, reg.Count)
        Assert.IsNull(reg.Resolve(uid))
    End Sub

    <TestMethod>
    Public Sub Count_TracksRegistrations()
        Dim reg As New UidRegistry()
        Assert.AreEqual(0, reg.Count)
        reg.GetOrCreateUid(New DependencyObject())
        reg.GetOrCreateUid(New DependencyObject())
        Assert.AreEqual(2, reg.Count)
    End Sub

End Class
