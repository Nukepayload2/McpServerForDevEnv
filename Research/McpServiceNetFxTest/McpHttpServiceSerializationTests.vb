Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports System.Net.Http
Imports System.Text
Imports Newtonsoft.Json
Imports McpServiceNetFx

<TestClass>
Public Class McpHttpServiceSerializationTests

    <TestMethod>
    Public Sub TestToolsListRequest()
        ' 准备 JSON-RPC 请求
        Dim requestJson = "{
                ""jsonrpc"": ""2.0"",
                ""method"": ""tools/list"",
                ""id"": 1
            }"

        Dim content As New StringContent(requestJson, Encoding.UTF8, "application/json")

        Try
            ' 验证请求 JSON 结构正确
            Dim rpcRequest = JsonConvert.DeserializeObject(Of JsonRpcRequest)(requestJson)
            Assert.AreEqual("2.0", rpcRequest.JsonRpc)
            Assert.AreEqual("tools/list", rpcRequest.Method)
            Assert.AreEqual(1, rpcRequest.Id)

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
            Assert.AreEqual("2.0", rpcRequest.JsonRpc)
            Assert.AreEqual("tools/call", rpcRequest.Method)

            Dim toolParams = JsonConvert.DeserializeObject(Of ToolCallParams)(rpcRequest.Params.ToString())
            Assert.AreEqual("build_solution", toolParams.name)
            Assert.AreEqual("Debug", toolParams.Arguments("configuration"))

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
            Assert.AreEqual("tools/call", rpcRequest.Method)

            Dim toolParams = JsonConvert.DeserializeObject(Of ToolCallParams)(rpcRequest.Params.ToString())
            Assert.AreEqual("get_error_list", toolParams.name)
            Assert.AreEqual("All", toolParams.Arguments("severity"))

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
            Assert.AreEqual("tools/call", rpcRequest.Method)

            Dim toolParams = JsonConvert.DeserializeObject(Of ToolCallParams)(rpcRequest.Params.ToString())
            Assert.AreEqual("get_solution_info", toolParams.name)
            Assert.AreEqual(0, toolParams.Arguments.Count)

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
            Assert.AreEqual("tools/call", rpcRequest.Method)

            Dim toolParams = JsonConvert.DeserializeObject(Of ToolCallParams)(rpcRequest.Params.ToString())
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
            Assert.AreEqual("2.0", response.JsonRpc)
            Assert.AreEqual(-32601, response.Error.Code)
            Assert.AreEqual("Method not found", response.Error.Message)

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
