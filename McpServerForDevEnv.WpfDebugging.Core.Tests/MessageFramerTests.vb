Imports System.IO
Imports Newtonsoft.Json.Linq
Imports McpServerForDevEnv.WpfDebugging

' MessageFramer 的 length-prefixed 帧读写往返测试。纯内存（MemoryStream），无副作用。

<TestClass>
Public Class MessageFramerTests

    <TestMethod>
    Public Async Function WriteThenRead_Roundtrips_Request() As Task
        Dim original As New WpfDebugRequest With {
            .Id = "req-1",
            .Method = WpfDebugMethods.TakeSnapshot,
            .Params = New JObject From {
                {"windowId", "w-0"},
                {"interestingOnly", True}
            }
        }

        Using ms As New MemoryStream()
            Await MessageFramer.WriteAsync(ms, original)
            ms.Position = 0
            Dim read As WpfDebugRequest = Await MessageFramer.ReadAsync(Of WpfDebugRequest)(ms)
            Assert.IsNotNull(read)
            Assert.AreEqual("req-1", read.Id)
            Assert.AreEqual(WpfDebugMethods.TakeSnapshot, read.Method)
            Assert.IsNotNull(read.Params)
            Assert.AreEqual("w-0", read.Params.Value(Of String)("windowId"))
            Assert.AreEqual(True, read.Params.Value(Of Boolean)("interestingOnly"))
        End Using
    End Function

    <TestMethod>
    Public Async Function WriteThenRead_Roundtrips_ResponseWithResult() As Task
        Dim original As New WpfDebugResponse With {
            .Id = "req-2",
            .Result = New JObject From {{"ok", True}}
        }

        Using ms As New MemoryStream()
            Await MessageFramer.WriteAsync(ms, original)
            ms.Position = 0
            Dim read As WpfDebugResponse = Await MessageFramer.ReadAsync(Of WpfDebugResponse)(ms)
            Assert.IsNotNull(read)
            Assert.AreEqual("req-2", read.Id)
            Assert.IsNull(read.Error)
            Assert.IsNotNull(read.Result)
            Assert.AreEqual(True, read.Result.Value(Of Boolean)("ok"))
        End Using
    End Function

    <TestMethod>
    Public Async Function WriteThenRead_Roundtrips_ResponseWithError() As Task
        Dim original As New WpfDebugResponse With {
            .Id = "req-3",
            .Error = New WpfDebugError With {.Code = -32000, .Message = "boom"}
        }

        Using ms As New MemoryStream()
            Await MessageFramer.WriteAsync(ms, original)
            ms.Position = 0
            Dim read As WpfDebugResponse = Await MessageFramer.ReadAsync(Of WpfDebugResponse)(ms)
            Assert.IsNotNull(read)
            Assert.IsNull(read.Result)
            Assert.IsNotNull(read.Error)
            Assert.AreEqual(-32000, read.Error.Code)
            Assert.AreEqual("boom", read.Error.Message)
        End Using
    End Function

    <TestMethod>
    Public Async Function WriteThenRead_Roundtrips_Event() As Task
        Dim original As New WpfDebugEvent With {
            .Event = "window_opened",
            .Data = New JObject From {{"windowId", "w-9"}}
        }

        Using ms As New MemoryStream()
            Await MessageFramer.WriteAsync(ms, original)
            ms.Position = 0
            Dim read As WpfDebugEvent = Await MessageFramer.ReadAsync(Of WpfDebugEvent)(ms)
            Assert.IsNotNull(read)
            Assert.AreEqual("window_opened", read.Event)
            Assert.AreEqual("w-9", read.Data.Value(Of String)("windowId"))
        End Using
    End Function

    <TestMethod>
    Public Async Function ReadAsync_ReturnsNothing_OnCleanClose() As Task
        ' 空流：连接一开始就关闭，应当返回 Nothing 而不是抛异常。
        Using ms As New MemoryStream()
            Dim read As WpfDebugRequest = Await MessageFramer.ReadAsync(Of WpfDebugRequest)(ms)
            Assert.IsNull(read)
        End Using
    End Function

    <TestMethod>
    Public Async Function WriteThenRead_MultipleFrames_InSequence() As Task
        ' 连续写多帧应能依次读回，验证长度前缀能正确分帧。
        Using ms As New MemoryStream()
            Await MessageFramer.WriteAsync(ms, New WpfDebugRequest With {.Id = "a", .Method = WpfDebugMethods.ListWindows})
            Await MessageFramer.WriteAsync(ms, New WpfDebugRequest With {.Id = "b", .Method = WpfDebugMethods.Click})
            ms.Position = 0

            Dim first As WpfDebugRequest = Await MessageFramer.ReadAsync(Of WpfDebugRequest)(ms)
            Dim second As WpfDebugRequest = Await MessageFramer.ReadAsync(Of WpfDebugRequest)(ms)
            Dim third As WpfDebugRequest = Await MessageFramer.ReadAsync(Of WpfDebugRequest)(ms)

            Assert.IsNotNull(first)
            Assert.AreEqual("a", first.Id)
            Assert.AreEqual(WpfDebugMethods.ListWindows, first.Method)
            Assert.IsNotNull(second)
            Assert.AreEqual("b", second.Id)
            Assert.AreEqual(WpfDebugMethods.Click, second.Method)
            Assert.IsNull(third)
        End Using
    End Function

    <TestMethod>
    Public Async Function HeaderUsesBigEndianFourBytes() As Task
        ' 直接核对线上字节：前 4 字节是大端长度。
        Dim payload As New WpfDebugRequest With {.Id = "x", .Method = WpfDebugMethods.Click}
        Using ms As New MemoryStream()
            Await MessageFramer.WriteAsync(ms, payload)
            ms.Position = 0
            Dim all As Byte() = ms.ToArray()
            Assert.IsTrue(all.Length >= 4)
            Dim declaredLen As Integer = (all(0) << 24) Or (all(1) << 16) Or (all(2) << 8) Or all(3)
            Assert.AreEqual(all.Length - 4, declaredLen)
        End Using
    End Function

End Class
