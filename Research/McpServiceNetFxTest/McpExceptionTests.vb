Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServiceNetFx

<TestClass>
Public Class McpExceptionTests
    <TestMethod>
    Public Sub TestMcpExceptionCreation()
        ' 测试自定义 MCP 异常的创建
        Dim ex1 As New McpException("Test error message")
        Assert.AreEqual("Test error message", ex1.Message)
        Assert.AreEqual("InternalError", ex1.ErrorCode)

        Dim ex2 As New McpException("Invalid params", McpErrorCode.InvalidParams)
        Assert.AreEqual("Invalid params", ex2.Message)
        Assert.AreEqual(McpErrorCode.InvalidParams, ex2.ErrorCode)

        Dim innerEx As New Exception("Inner exception")
        Dim ex3 As New McpException("Wrapped error", McpErrorCode.InternalError, innerEx)
        Assert.AreEqual("Wrapped error", ex3.Message)
        Assert.AreEqual(innerEx, ex3.InnerException)
    End Sub

    <TestMethod>
    Public Sub TestMcpErrorCodeConstants()
        ' 测试 MCP 错误代码常量
        Assert.AreEqual("-32602", McpErrorCode.InvalidParams)
        Assert.AreEqual("-32603", McpErrorCode.InternalError)
        Assert.AreEqual("-32601", McpErrorCode.MethodNotFound)
        Assert.AreEqual("-32600", McpErrorCode.InvalidRequest)
        Assert.AreEqual("-32700", McpErrorCode.ParseError)
    End Sub
End Class
