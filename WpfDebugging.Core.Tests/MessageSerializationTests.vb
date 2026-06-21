Imports Newtonsoft.Json
Imports Newtonsoft.Json.Linq
Imports McpServerForDevEnv.WpfDebugging

' IPC 消息类型经 Newtonsoft.Json 序列化/反序列化往返测试。纯内存，无副作用。

<TestClass>
Public Class MessageSerializationTests

    <TestMethod>
    Public Sub Request_Serializes_With_Expected_Field_Names()
        Dim req As New WpfDebugRequest With {
            .Id = "id-1",
            .Method = WpfDebugMethods.Fill,
            .Params = New JObject From {{"uid", "u-1"}, {"value", "hello"}}
        }
        Dim json As String = JsonConvert.SerializeObject(req)
        Dim jo As JObject = JObject.Parse(json)

        Assert.AreEqual("id-1", jo.Value(Of String)("id"))
        Assert.AreEqual(WpfDebugMethods.Fill, jo.Value(Of String)("method"))
        Assert.AreEqual("u-1", jo("params").Value(Of String)("uid"))
    End Sub

    <TestMethod>
    Public Sub Request_Omits_Null_Params_By_Ignore_Attribute()
        Dim req As New WpfDebugRequest With {.Id = "id-2", .Method = WpfDebugMethods.ListWindows, .Params = Nothing}
        Dim json As String = JsonConvert.SerializeObject(req)
        Dim jo As JObject = JObject.Parse(json)

        Assert.IsFalse(jo.ContainsKey("params"))
    End Sub

    <TestMethod>
    Public Sub Response_Roundtrips_Through_Json()
        Dim resp As New WpfDebugResponse With {
            .Id = "id-3",
            .Result = New JObject From {{"count", 3}}
        }
        Dim json As String = JsonConvert.SerializeObject(resp)
        Dim back As WpfDebugResponse = JsonConvert.DeserializeObject(Of WpfDebugResponse)(json)

        Assert.AreEqual("id-3", back.Id)
        Assert.IsNotNull(back.Result)
        Assert.AreEqual(3, back.Result.Value(Of Integer)("count"))
        Assert.IsNull(back.Error)
    End Sub

    <TestMethod>
    Public Sub Error_Roundtrips_With_Data()
        Dim err As New WpfDebugError With {
            .Code = 42,
            .Message = "nope",
            .Data = New JObject From {{"detail", "x"}}
        }
        Dim json As String = JsonConvert.SerializeObject(err)
        Dim back As WpfDebugError = JsonConvert.DeserializeObject(Of WpfDebugError)(json)

        Assert.AreEqual(42, back.Code)
        Assert.AreEqual("nope", back.Message)
        Assert.IsNotNull(back.Data)
        Assert.AreEqual("x", back.Data.Value(Of String)("detail"))
    End Sub

    <TestMethod>
    Public Sub Event_Roundtrips_Through_Json()
        Dim ev As New WpfDebugEvent With {
            .Event = "unhandled_exception",
            .Data = New JObject From {{"message", "boom"}}
        }
        Dim json As String = JsonConvert.SerializeObject(ev)
        Dim back As WpfDebugEvent = JsonConvert.DeserializeObject(Of WpfDebugEvent)(json)

        Assert.AreEqual("unhandled_exception", back.Event)
        Assert.AreEqual("boom", back.Data.Value(Of String)("message"))
    End Sub

    <TestMethod>
    Public Sub ProtocolConstants_Have_Expected_Values()
        Assert.AreEqual("mcpserverfordevenv.wpfdebug.v1", WpfDebugProtocol.PipeName)
        Assert.AreEqual("1", WpfDebugProtocol.ProtocolVersion)
    End Sub

End Class
