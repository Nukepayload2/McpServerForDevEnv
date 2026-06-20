using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using McpServerForDevEnv.WpfDebugging.Target;

namespace McpServerForDevEnv.WpfDebugging.Target.Tests;

// WpfDispatcher 在「无 WPF Application」回退路径下的测试。
// 测试进程不启动 WPF Application，Application.Current 为 null，故 InvokeAsync 走 Task.Run 回退。
// 这条路径纯内存（线程池跑委托），不连 pipe、不写文件、不碰 UI 线程，无副作用。
// （真实 Dispatcher 路径需要 WPF Application + UI 线程，留给端到端验证 #24。）

[TestClass]
public class WpfDispatcherTests
{
    [TestMethod]
    public void GetDispatcher_ReturnsNull_WhenNoApplication()
    {
        // 测试进程未起 WPF Application。
        var d = new WpfDispatcher();
        Assert.IsNull(d.GetDispatcher());
    }

    [TestMethod]
    public async Task InvokeAsync_Fallback_RunsAndReturnsValue()
    {
        var d = new WpfDispatcher();
        int result = await d.InvokeAsync<int>(() => 42);
        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public async Task InvokeAsync_Action_Fallback_Completes()
    {
        var d = new WpfDispatcher();
        bool hit = false;
        await d.InvokeAsync(new Action(() => hit = true));
        Assert.IsTrue(hit);
    }

    [TestMethod]
    public void InvokeAsync_NullFunc_Throws()
    {
        var d = new WpfDispatcher();
        Assert.ThrowsException<ArgumentNullException>(() => d.InvokeAsync<int>(null));
    }
}
