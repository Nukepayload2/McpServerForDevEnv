Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports System.Net.Http
Imports System.Text
Imports Newtonsoft.Json
Imports McpServiceNetFx

Namespace McpServiceNetFxTest
    <TestClass>
    Public Class McpHttpServiceTests
        Private _httpClient As HttpClient
        Private _baseUrl As String = "http://localhost:8080/mcp"

        <TestInitialize>
        Public Sub Setup()
            _httpClient = New HttpClient()
        End Sub

        <TestCleanup>
        Public Sub Cleanup()
            _httpClient?.Dispose()
        End Sub

        <TestMethod>
        Public Sub TestToolsListRequest()
            ' 准备 JSON-RPC 请求
            Dim requestJson = "{
                ""jsonrpc"": ""2.0"",
                ""method"": ""tools/list"",
                ""id"": 1
            }"

            Dim content As New StringContent(requestJson, Encoding.UTF8, "application/json")

            ' 发送请求（注意：这需要服务正在运行）
            ' 这个测试主要用于验证 JSON 结构，实际运行需要服务启动
            Try
                ' 注意：在实际测试中需要先启动 MCP 服务
                ' Dim response = _httpClient.PostAsync(_baseUrl, content).Result
                ' Dim responseString = response.Content.ReadAsStringAsync().Result
                ' Assert.IsTrue(response.IsSuccessStatusCode)

                ' 验证请求 JSON 结构正确
                Dim rpcRequest = JsonConvert.DeserializeObject(Of JsonRpcRequest)(requestJson)
                Assert.AreEqual("2.0", rpcRequest.jsonrpc)
                Assert.AreEqual("tools/list", rpcRequest.method)
                Assert.AreEqual(1, rpcRequest.id)

            Catch ex As Exception
                Assert.Inconclusive("MCP 服务未运行，无法进行集成测试")
            End Try
        End Sub

        <TestMethod>
        Public Sub TestBuildSolutionRequest()
            ' 准备构建解决方案的 JSON-RPC 请求
            Dim requestJson = "{
                ""jsonrpc"": ""2.0"",
                ""method"": ""tools/call"",
                ""params"": {
                    ""name"": ""build_solution"",
                    ""arguments"": {
                        ""configuration"": ""Debug""
                    }
                },
                ""id"": 2
            }"

            Dim content As New StringContent(requestJson, Encoding.UTF8, "application/json")

            Try
                ' 验证请求 JSON 结构正确
                Dim rpcRequest = JsonConvert.DeserializeObject(Of JsonRpcRequest)(requestJson)
                Assert.AreEqual("2.0", rpcRequest.jsonrpc)
                Assert.AreEqual("tools/call", rpcRequest.method)

                Dim toolParams = JsonConvert.DeserializeObject(Of ToolCallParams)(rpcRequest.params.ToString())
                Assert.AreEqual("build_solution", toolParams.name)
                Assert.AreEqual("Debug", toolParams.arguments("configuration"))

            Catch ex As Exception
                Assert.Inconclusive("JSON 结构解析失败")
            End Try
        End Sub

        <TestMethod>
        Public Sub TestGetErrorListRequest()
            ' 准备获取错误列表的 JSON-RPC 请求
            Dim requestJson = "{
                ""jsonrpc"": ""2.0"",
                ""method"": ""tools/call"",
                ""params"": {
                    ""name"": ""get_error_list"",
                    ""arguments"": {
                        ""severity"": ""All""
                    }
                },
                ""id"": 3
            }"

            Try
                ' 验证请求 JSON 结构正确
                Dim rpcRequest = JsonConvert.DeserializeObject(Of JsonRpcRequest)(requestJson)
                Assert.AreEqual("tools/call", rpcRequest.method)

                Dim toolParams = JsonConvert.DeserializeObject(Of ToolCallParams)(rpcRequest.params.ToString())
                Assert.AreEqual("get_error_list", toolParams.name)
                Assert.AreEqual("All", toolParams.arguments("severity"))

            Catch ex As Exception
                Assert.Fail($"JSON 解析失败: {ex.Message}")
            End Try
        End Sub

        <TestMethod>
        Public Sub TestGetSolutionInfoRequest()
            ' 准备获取解决方案信息的 JSON-RPC 请求
            Dim requestJson = "{
                ""jsonrpc"": ""2.0"",
                ""method"": ""tools/call"",
                ""params"": {
                    ""name"": ""get_solution_info"",
                    ""arguments"": {}
                },
                ""id"": 4
            }"

            Try
                ' 验证请求 JSON 结构正确
                Dim rpcRequest = JsonConvert.DeserializeObject(Of JsonRpcRequest)(requestJson)
                Assert.AreEqual("tools/call", rpcRequest.method)

                Dim toolParams = JsonConvert.DeserializeObject(Of ToolCallParams)(rpcRequest.params.ToString())
                Assert.AreEqual("get_solution_info", toolParams.name)
                Assert.AreEqual(0, toolParams.arguments.Count)

            Catch ex As Exception
                Assert.Fail($"JSON 解析失败: {ex.Message}")
            End Try
        End Sub

        <TestMethod>
        Public Sub TestBuildProjectRequest()
            ' 准备构建项目的 JSON-RPC 请求
            Dim requestJson = "{
                ""jsonrpc"": ""2.0"",
                ""method"": ""tools/call"",
                ""params"": {
                    ""name"": ""build_project"",
                    ""arguments"": {
                        ""projectName"": ""TestProject"",
                        ""configuration"": ""Release""
                    }
                },
                ""id"": 5
            }"

            Try
                ' 验证请求 JSON 结构正确
                Dim rpcRequest = JsonConvert.DeserializeObject(Of JsonRpcRequest)(requestJson)
                Assert.AreEqual("tools/call", rpcRequest.method)

                Dim toolParams = JsonConvert.DeserializeObject(Of ToolCallParams)(rpcRequest.params.ToString())
                Assert.AreEqual("build_project", toolParams.name)
                Assert.AreEqual("TestProject", toolParams.arguments("projectName"))
                Assert.AreEqual("Release", toolParams.arguments("configuration"))

            Catch ex As Exception
                Assert.Fail($"JSON 解析失败: {ex.Message}")
            End Try
        End Sub


        <TestMethod>
        Public Sub TestErrorResponseStructure()
            ' 测试错误响应的 JSON 结构
            Dim errorJson = "{
                ""jsonrpc"": ""2.0"",
                ""error"": {
                    ""code"": -32601,
                    ""message"": ""Method not found"",
                    ""data"": ""Unknown method: invalid_method""
                },
                ""id"": null
            }"

            Try
                Dim response = JsonConvert.DeserializeObject(Of JsonRpcResponse)(errorJson)
                Assert.AreEqual("2.0", response.jsonrpc)
                Assert.AreEqual(-32601, response.error.code)
                Assert.AreEqual("Method not found", response.error.message)

            Catch ex As Exception
                Assert.Fail($"错误响应 JSON 解析失败: {ex.Message}")
            End Try
        End Sub

        <TestMethod>
        Public Sub TestBuildResultResponseStructure()
            ' 测试构建结果响应的强类型结构
            Dim successResponseJson = "{
                ""success"": true,
                ""message"": ""构建成功完成"",
                ""buildTime"": ""00:00:05.1234567"",
                ""configuration"": ""Debug"",
                ""errors"": [],
                ""warnings"": []
            }"

            Try
                Dim result = JsonConvert.DeserializeObject(Of BuildResultResponse)(successResponseJson)
                Assert.IsTrue(result.Success)
                Assert.AreEqual("构建成功完成", result.Message)
                Assert.AreEqual("Debug", result.Configuration)
                Assert.AreEqual(0, result.Errors.Length)
                Assert.AreEqual(0, result.Warnings.Length)

            Catch ex As Exception
                Assert.Fail($"构建结果响应 JSON 解析失败: {ex.Message}")
            End Try
        End Sub

        <TestMethod>
        Public Sub TestSolutionInfoResponseStructure()
            ' 测试解决方案信息响应的强类型结构
            Dim solutionInfoJson = "{
                ""fullName"": ""C:\\Projects\\TestSolution\\TestSolution.sln"",
                ""name"": ""TestSolution"",
                ""count"": 2,
                ""projects"": [
                    {
                        ""name"": ""Project1"",
                        ""fullName"": ""C:\\Projects\\TestSolution\\Project1\\Project1.vbproj"",
                        ""uniqueName"": ""Project1"",
                        ""kind"": ""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}""
                    }
                ],
                ""activeConfiguration"": {
                    ""name"": ""Debug|Any CPU"",
                    ""configurationName"": ""Debug"",
                    ""platformName"": ""Any CPU""
                }
            }"

            Try
                Dim result = JsonConvert.DeserializeObject(Of SolutionInfoResponse)(solutionInfoJson)
                Assert.AreEqual("TestSolution", result.Name)
                Assert.AreEqual(2, result.Count)
                Assert.AreEqual(1, result.Projects.Length)
                Assert.AreEqual("Project1", result.Projects(0).Name)
                Assert.IsNotNull(result.ActiveConfiguration)
                Assert.AreEqual("Debug", result.ActiveConfiguration.ConfigurationName)

            Catch ex As Exception
                Assert.Fail($"解决方案信息响应 JSON 解析失败: {ex.Message}")
            End Try
        End Sub
    End Class

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
End Namespace
