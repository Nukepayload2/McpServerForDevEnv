using System.Globalization;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using McpServerForDevEnv.WpfDebugging.Target;

namespace McpServerForDevEnv.WpfDebugging.Target.Tests;

// UidRegistry 的纯逻辑测试。实例化 DependencyObject 不启动 Dispatcher / 不创建窗口 /
// 不连 pipe / 不写文件，属纯内存对象操作，无副作用。

[TestClass]
public class UidRegistryTests
{
    [TestMethod]
    public void GetOrCreateUid_SameElement_ReturnsSameUid()
    {
        var reg = new UidRegistry();
        var el = new DependencyObject();
        string uid1 = reg.GetOrCreateUid(el);
        string uid2 = reg.GetOrCreateUid(el);

        Assert.IsFalse(string.IsNullOrEmpty(uid1));
        Assert.AreEqual(uid1, uid2);
    }

    [TestMethod]
    public void GetOrCreateUid_DifferentElements_ReturnDifferentUids()
    {
        var reg = new UidRegistry();
        var a = new DependencyObject();
        var b = new DependencyObject();
        string uidA = reg.GetOrCreateUid(a);
        string uidB = reg.GetOrCreateUid(b);

        Assert.AreNotEqual(uidA, uidB);
    }

    [TestMethod]
    public void Resolve_KnownUid_ReturnsElement()
    {
        var reg = new UidRegistry();
        var el = new DependencyObject();
        string uid = reg.GetOrCreateUid(el);

        DependencyObject back = reg.Resolve(uid);
        Assert.IsNotNull(back);
        Assert.AreSame(el, back);
    }

    [TestMethod]
    public void Resolve_UnknownUid_ReturnsNull()
    {
        var reg = new UidRegistry();
        Assert.IsNull(reg.Resolve("999999999"));
    }

    [TestMethod]
    public void Resolve_InvalidUid_ReturnsNull()
    {
        var reg = new UidRegistry();
        Assert.IsNull(reg.Resolve("not-a-number"));
        Assert.IsNull(reg.Resolve(""));
        Assert.IsNull(reg.Resolve(null));
    }

    [TestMethod]
    public void Uid_Matches_HashCode_String_Form()
    {
        var reg = new UidRegistry();
        var el = new DependencyObject();
        string uid = reg.GetOrCreateUid(el);

        Assert.AreEqual(el.GetHashCode().ToString(CultureInfo.InvariantCulture), uid);
    }

    [TestMethod]
    public void Clear_DropsAllEntries()
    {
        var reg = new UidRegistry();
        var el = new DependencyObject();
        string uid = reg.GetOrCreateUid(el);
        Assert.AreEqual(1, reg.Count);

        reg.Clear();
        Assert.AreEqual(0, reg.Count);
        Assert.IsNull(reg.Resolve(uid));
    }

    [TestMethod]
    public void Count_TracksRegistrations()
    {
        var reg = new UidRegistry();
        Assert.AreEqual(0, reg.Count);
        reg.GetOrCreateUid(new DependencyObject());
        reg.GetOrCreateUid(new DependencyObject());
        Assert.AreEqual(2, reg.Count);
    }
}
