Imports Newtonsoft.Json
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServerForDevEnv.WpfDebugging
Imports McpServerForDevEnv.WpfDebugging.Target

' HandshakeInfo 序列化往返 + WpfDebugHostConfig 默认值测试。纯内存，无副作用。

<TestClass>
Public Class HandshakeAndConfigTests

    <TestMethod>
    Public Sub HandshakeInfo_Roundtrips_Through_Json()
        Dim info As New HandshakeInfo With {
            .ProtocolVersion = WpfDebugProtocol.ProtocolVersion,
            .Pid = 1234,
            .MainWindowTitle = "Hello"
        }
        Dim json As String = JsonConvert.SerializeObject(info)
        Dim back = JsonConvert.DeserializeObject(Of HandshakeInfo)(json)

        Assert.AreEqual(WpfDebugProtocol.ProtocolVersion, back.ProtocolVersion)
        Assert.AreEqual(1234, back.Pid)
        Assert.AreEqual("Hello", back.MainWindowTitle)
    End Sub

    <TestMethod>
    Public Sub HandshakeInfo_Omits_Null_Title()
        Dim info As New HandshakeInfo With {
            .ProtocolVersion = "1",
            .Pid = 7,
            .MainWindowTitle = Nothing
        }
        Dim json As String = JsonConvert.SerializeObject(info)
        Assert.IsFalse(json.Contains("mainWindowTitle"))
    End Sub

    <TestMethod>
    Public Sub Config_Defaults()
        Dim cfg As New WpfDebugHostConfig()
        Assert.IsTrue(cfg.EnableScripting)
        Assert.AreEqual(1, cfg.MaxConcurrentConnections)
    End Sub

End Class
