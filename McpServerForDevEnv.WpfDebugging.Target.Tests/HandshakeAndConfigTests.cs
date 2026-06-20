using Newtonsoft.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using McpServerForDevEnv.WpfDebugging;
using McpServerForDevEnv.WpfDebugging.Target;

namespace McpServerForDevEnv.WpfDebugging.Target.Tests;

// HandshakeInfo 序列化往返 + WpfDebugHostConfig 默认值测试。纯内存，无副作用。

[TestClass]
public class HandshakeAndConfigTests
{
    [TestMethod]
    public void HandshakeInfo_Roundtrips_Through_Json()
    {
        var info = new HandshakeInfo
        {
            ProtocolVersion = WpfDebugProtocol.ProtocolVersion,
            Pid = 1234,
            MainWindowTitle = "Hello"
        };
        string json = JsonConvert.SerializeObject(info);
        var back = JsonConvert.DeserializeObject<HandshakeInfo>(json);

        Assert.AreEqual(WpfDebugProtocol.ProtocolVersion, back!.ProtocolVersion);
        Assert.AreEqual(1234, back.Pid);
        Assert.AreEqual("Hello", back.MainWindowTitle);
    }

    [TestMethod]
    public void HandshakeInfo_Omits_Null_Title()
    {
        var info = new HandshakeInfo
        {
            ProtocolVersion = "1",
            Pid = 7,
            MainWindowTitle = null
        };
        string json = JsonConvert.SerializeObject(info);
        Assert.IsFalse(json.Contains("mainWindowTitle"));
    }

    [TestMethod]
    public void Config_Defaults()
    {
        var cfg = new WpfDebugHostConfig();
        Assert.IsTrue(cfg.EnableScripting);
        Assert.AreEqual(1, cfg.MaxConcurrentConnections);
    }
}
