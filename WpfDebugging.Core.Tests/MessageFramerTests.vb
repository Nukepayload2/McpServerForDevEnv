Imports System.IO
Imports System.Text
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
            ' 解码前先把 Byte 转 Integer：VB 的 << 对 Byte 左操作数返回 Byte，直接 Byte<<8 会溢出。
            Dim declaredLen As Integer = (CInt(all(0)) << 24) Or (CInt(all(1)) << 16) Or (CInt(all(2)) << 8) Or CInt(all(3))
            Assert.AreEqual(all.Length - 4, declaredLen)
        End Using
    End Function

    ''' <summary>
    ''' 大 payload（> 255 字节）往返：覆盖 4 字节大端长度的非零高位字节编码/解码。
    ''' 这是 #24 联调暴露的回归点——body 超过 255 字节时，header 的第 2/3 字节非零，
    ''' 旧实现在 WriteAsync 用 CByte(body.Length) 直接转（>255 抛 OverflowException）、
    ''' ReadAsync 用 header(i)<<N（Byte 左移返回 Byte，高位溢出截断）解码出错误长度，
    ''' 导致大响应（如 take_snapshot 的完整 snapshot 树）序列化后主控读到的 JSON 被截断、
    ''' 反序列化抛 JsonReaderException，连接随之断开。本测试用远超 255 字节的 payload
    ''' 确保编码-解码往返精确还原。
    ''' </summary>
    <TestMethod>
    Public Async Function WriteThenRead_Roundtrips_LargePayload_Over255Bytes() As Task
        ' 构造一个明显超过 255 字节的 payload：嵌套大 JObject（模拟 take_snapshot 的 snapshot 树）。
        Dim bigContent As New StringBuilder()
        bigContent.Append("x"c, 5000)  ' 5000 字符的值，确保总 payload 远超 255 字节
        Dim result As New JObject From {{"text", bigContent.ToString()}}
        Dim original As New WpfDebugResponse With {.Id = "large-1", .Result = result}

        Using ms As New MemoryStream()
            Await MessageFramer.WriteAsync(ms, original)
            ms.Position = 0
            Dim all As Byte() = ms.ToArray()

            ' 校验线上字节：总长 > 255，且前 4 字节解码出的长度 == body 实际字节数。
            Assert.IsTrue(all.Length > 255, $"payload 应超过 255 字节，实际 {all.Length}")
            Dim declaredLen As Integer = (CInt(all(0)) << 24) Or (CInt(all(1)) << 16) Or (CInt(all(2)) << 8) Or CInt(all(3))
            Assert.AreEqual(all.Length - 4, declaredLen, "header 声明长度应等于 body 字节数")

            ' 往返读回应精确还原（不被截断）。
            ms.Position = 0
            Dim read As WpfDebugResponse = Await MessageFramer.ReadAsync(Of WpfDebugResponse)(ms)
            Assert.IsNotNull(read)
            Assert.AreEqual("large-1", read.Id)
            Assert.IsNotNull(read.Result)
            Assert.AreEqual(bigContent.ToString(), read.Result.Value(Of String)("text"))
        End Using
    End Function

    ''' <summary>
    ''' 多个边界长度值（255/256/65535/65536）的 header 编码-解码往返，
    ''' 覆盖字节进位的所有边界（Byte 溢出阈值、单字节→双字节、双字节→三字节）。
    ''' </summary>
    <TestMethod>
    Public Async Function WriteThenRead_Roundtrips_LengthBoundaries() As Task
        ' 不同大小的文本值产生不同 body 长度，逐一验证往返精确。
        Dim sizes As Integer() = {0, 1, 200, 255, 256, 1000, 65535, 65536}
        For Each size As Integer In sizes
            Dim sb As New StringBuilder()
            sb.Append("a"c, size)
            Dim payload As New WpfDebugResponse With {
                .Id = $"s-{size}",
                .Result = New JObject From {{"v", sb.ToString()}}
            }
            Using ms As New MemoryStream()
                Await MessageFramer.WriteAsync(ms, payload)
                ms.Position = 0
                Dim read As WpfDebugResponse = Await MessageFramer.ReadAsync(Of WpfDebugResponse)(ms)
                Assert.IsNotNull(read, $"size={size}: 读回为空")
                Assert.AreEqual($"s-{size}", read.Id, $"size={size}: id 不匹配")
                Assert.AreEqual(sb.ToString(), read.Result?.Value(Of String)("v"), $"size={size}: 值不匹配")
            End Using
        Next
    End Function

End Class
