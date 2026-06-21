Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServiceNetFx
Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

''' <summary>
''' WPF 调试响应解析（OkResult 嵌套约定）的纯函数测试。
''' 无副作用：不开 pipe、不启进程、不碰文件，只构造 JObject/JToken 验证解析逻辑。
''' </summary>
''' <remarks>
''' 【为何重点测这里】被控端 WpfDebugPipeServer.OkResult 把每个方法返回值统一包成
''' Result = { "result": <JToken> }，主控取业务值必须 response.Result("result") 嵌套取。
''' 这是 handoff 点名的易错点，单测锁死这条约定。
''' </remarks>
<TestClass>
Public Class WpfDebugResultReaderTests

    ''' <summary>业务返回值是对象时，嵌套取出来应等于该对象。</summary>
    <TestMethod>
    Public Sub GetPayload_ObjectResult_UnwrapsNestedResultKey()
        ' 模拟被控端 OkResult({"name": "btn1", "count": 3}) 产出的响应。
        Dim inner As New JObject()
        inner("name") = "btn1"
        inner("count") = 3

        Dim outer As New JObject()
        outer("result") = inner ' OkResult 嵌套约定

        Dim response As New WpfDebugResponse With {.Id = "1", .Result = outer}

        Dim payload As JToken = WpfDebugResultReader.GetPayload(response)

        Assert.IsNotNull(payload)
        Assert.AreEqual("btn1", payload.Value(Of String)("name"))
        Assert.AreEqual(3, payload.Value(Of Integer)("count"))
    End Sub

    ''' <summary>业务返回值是数组时，嵌套取出来应等于该数组。</summary>
    <TestMethod>
    Public Sub GetPayload_ArrayResult_UnwrapsNestedResultKey()
        Dim inner As New JArray()
        inner.Add("win1")
        inner.Add("win2")

        Dim outer As New JObject()
        outer("result") = inner

        Dim response As New WpfDebugResponse With {.Id = "1", .Result = outer}

        Dim payload As JToken = WpfDebugResultReader.GetPayload(response)

        Assert.IsNotNull(payload)
        Assert.AreEqual(JTokenType.Array, payload.Type)
        Assert.AreEqual(2, payload.Count())
    End Sub

    ''' <summary>业务返回值是标量时，嵌套取出来应等于该标量。</summary>
    <TestMethod>
    Public Sub GetPayload_ScalarResult_UnwrapsNestedResultKey()
        Dim outer As New JObject()
        outer("result") = 42

        Dim response As New WpfDebugResponse With {.Id = "1", .Result = outer}

        Dim payload As JToken = WpfDebugResultReader.GetPayload(response)

        Assert.IsNotNull(payload)
        Assert.AreEqual(42, payload.Value(Of Integer)())
    End Sub

    ''' <summary>Sub 类方法无业务返回值：被控端产空 JObject（无 "result" 键），payload 为 Nothing。</summary>
    <TestMethod>
    Public Sub GetPayload_EmptyOkResult_ReturnsNothing()
        Dim outer As New JObject() ' OkResult(Nothing) 产空 JObject

        Dim response As New WpfDebugResponse With {.Id = "1", .Result = outer}

        Dim payload As JToken = WpfDebugResultReader.GetPayload(response)

        Assert.IsNull(payload)
    End Sub

    ''' <summary>响应带 Error 时不应当成功解析，应抛异常。</summary>
    <TestMethod>
    Public Sub GetPayload_WhenError_ProtectsAgainstTreatingAsSuccess()
        Dim response As New WpfDebugResponse With {
            .Id = "1",
            .Error = New WpfDebugError With {.Code = -32603, .Message = "boom"}
        }

        ' 业务代码应先检查 Error 或由 WpfDebugProxy 抛 WpfDebugRemoteException；
        ' 但 GetPayload 作为防御性解析，遇到 Error 也应报错而非返回错误数据。
        Try
            WpfDebugResultReader.GetPayload(response)
            Assert.Fail("带 Error 的响应不应被当作成功解析")
        Catch ex As InvalidOperationException
            StringAssert.Contains(ex.Message, "boom")
        End Try
    End Sub

    ''' <summary>成功响应缺 Result 字段（协议异常）应报错。</summary>
    <TestMethod>
    Public Sub GetPayload_MissingResult_Throws()
        Dim response As New WpfDebugResponse With {.Id = "1", .Result = Nothing}

        Try
            WpfDebugResultReader.GetPayload(response)
            Assert.Fail("缺 Result 的响应不应解析成功")
        Catch ex As InvalidOperationException
            StringAssert.Contains(ex.Message, "Result")
        End Try
    End Sub

    ''' <summary>GetPayloadAs 强转标量成功。</summary>
    <TestMethod>
    Public Sub GetPayloadAs_Scalar_ConvertsToType()
        Dim outer As New JObject()
        outer("result") = "hello"
        Dim response As New WpfDebugResponse With {.Id = "1", .Result = outer}

        Dim value As String = WpfDebugResultReader.GetPayloadAs(response, "default")

        Assert.AreEqual("hello", value)
    End Sub

    ''' <summary>GetPayloadAs 在无 payload（Sub 方法）时返回默认值。</summary>
    <TestMethod>
    Public Sub GetPayloadAs_WhenNoPayload_ReturnsDefault()
        Dim outer As New JObject()
        Dim response As New WpfDebugResponse With {.Id = "1", .Result = outer}

        Dim value As String = WpfDebugResultReader.GetPayloadAs(response, "fallback")

        Assert.AreEqual("fallback", value)
    End Sub

    ''' <summary>response 为 Nothing 应抛 ArgumentNullException。</summary>
    <TestMethod>
    Public Sub GetPayload_NullResponse_Throws()
        Try
            WpfDebugResultReader.GetPayload(Nothing)
            Assert.Fail("Nothing 响应不应被接受")
        Catch ex As ArgumentNullException
            ' 预期
        End Try
    End Sub
End Class
