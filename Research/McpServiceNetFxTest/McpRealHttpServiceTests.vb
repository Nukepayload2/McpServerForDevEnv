Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports System.Net.Http
Imports System.Text
Imports Newtonsoft.Json
Imports McpServiceNetFx
Imports System.Net.Sockets
Imports System.Threading.Tasks

''' <summary>
''' 真实 HTTP 服务测试类 - 用于测试运行在端口 38080 的 MCP HTTP 服务
''' 注意：这些测试需要服务正在运行才能成功执行
''' </summary>
<TestClass>
Public Class McpRealHttpServiceTests
    Private _httpClient As HttpClient
    Private Const DEFAULT_PORT As Integer = 38080
    Private _baseUrl As String
    Private _serviceAvailable As Boolean?

    <TestInitialize>
    Public Sub Setup()
        Dim handler As New HttpClientHandler With {.UseProxy = False}
        _httpClient = New HttpClient(handler)
        _httpClient.Timeout = TimeSpan.FromSeconds(10) ' 减少超时时间以便快速检测服务状态
        _baseUrl = $"http://localhost:{DEFAULT_PORT}/mcp/"
        _serviceAvailable = Nothing ' 重置服务状态
    End Sub

    <TestCleanup>
    Public Sub Cleanup()
        _httpClient?.Dispose()
    End Sub

    ''' <summary>
    ''' 检查服务端口是否有监听，缓存结果以提高性能
    ''' </summary>
    Private Function IsServiceAvailable() As Boolean
        If _serviceAvailable.HasValue Then
            Return _serviceAvailable.Value
        End If

        Try
            Using tcpClient As New TcpClient()
                ' 设置短超时时间以便快速检测
                tcpClient.ReceiveTimeout = 1000 ' 1秒
                tcpClient.SendTimeout = 1000 ' 1秒

                ' 尝试连接到默认端口
                tcpClient.Connect("localhost", DEFAULT_PORT)

                ' 不仅检查连接，还要确保端口真正可用
                If tcpClient.Connected Then
                    ' 连接成功，关闭连接（端口确实有监听）
                    _serviceAvailable = True
                Else
                    _serviceAvailable = False
                End If
            End Using
        Catch ex As SocketException
            ' 端口未监听或连接被拒绝
            _serviceAvailable = False
        Catch ex As ObjectDisposedException
            ' 连接过程中被释放
            _serviceAvailable = False
        Catch ex As InvalidOperationException
            ' 无效操作
            _serviceAvailable = False
        Catch ex As TaskCanceledException
            ' 连接超时
            _serviceAvailable = False
        Catch ex As Exception
            ' 其他异常
            _serviceAvailable = False
        End Try

        Return _serviceAvailable.Value
    End Function

    ''' <summary>
    ''' 测试服务是否可访问
    ''' </summary>
    <TestMethod>
    Public Async Function TestServiceAvailability() As Task
        If Not IsServiceAvailable() Then
            Assert.Inconclusive($"MCP HTTP 服务未在端口 {DEFAULT_PORT} 运行，跳过集成测试")
            Return
        End If

        Try
            ' 服务可用，进行详细测试
            Dim pingRequest = CreateJsonRpcRequest("ping", Nothing, 1)
            Dim content As New StringContent(pingRequest, Encoding.UTF8, "application/json")

            Dim response = Await _httpClient.PostAsync(_baseUrl, content)
            Dim responseString = Await response.Content.ReadAsStringAsync()

            ' 如果服务运行但返回错误状态，这是真正的错误
            Assert.IsTrue(response.IsSuccessStatusCode, $"服务可访问但返回错误状态码: {response.StatusCode}")

            Dim rpcResponse = JsonConvert.DeserializeObject(Of JsonRpcResponse)(responseString)
            Assert.IsNotNull(rpcResponse, "服务响应不应为空")
            Assert.AreEqual("2.0", rpcResponse.JsonRpc, "响应应为 JSON-RPC 2.0 格式")
            Assert.AreEqual(CObj(1), rpcResponse.Id, "响应ID应匹配请求ID")
            Assert.IsNotNull(rpcResponse.Result, "Ping 响应应有结果")

        Catch ex As HttpRequestException
            Assert.Fail($"网络请求失败: {GetFullExceptionMessage(ex)}")
        Catch ex As JsonException
            Assert.Fail($"JSON 响应格式错误: {GetFullExceptionMessage(ex)}")
        Catch ex As Exception
            Assert.Fail($"测试执行失败: {GetFullExceptionMessage(ex)}")
        End Try
    End Function

    ''' <summary>
    ''' 测试完整的初始化流程
    ''' </summary>
    <TestMethod>
    Public Async Function TestFullInitializationFlow() As Task
        If Not IsServiceAvailable() Then
            Assert.Inconclusive($"MCP HTTP 服务未在端口 {DEFAULT_PORT} 运行，跳过集成测试")
            Return
        End If

        Try
            ' 1. 发送 initialize 请求
            Dim initParams = New Dictionary(Of String, Object) From {
                {"protocolVersion", "2024-10-07"},
                {"capabilities", New Dictionary(Of String, Object) From {
                    {"tools", New Dictionary(Of String, Object)}
                }},
                {"clientInfo", New Dictionary(Of String, Object) From {
                    {"name", "Test Client"},
                    {"version", "1.0.0"}
                }}
            }

            Dim initRequest = CreateJsonRpcRequest("initialize", initParams, 1)
            Dim initContent As New StringContent(initRequest, Encoding.UTF8, "application/json")

            Dim initResponse = Await _httpClient.PostAsync(_baseUrl, initContent)
            Dim initResponseString = Await initResponse.Content.ReadAsStringAsync()

            Assert.IsTrue(initResponse.IsSuccessStatusCode, $"Initialize 请求失败，状态码: {initResponse.StatusCode}")

            Dim initRpcResponse = JsonConvert.DeserializeObject(Of JsonRpcResponse)(initResponseString)
            Assert.IsNotNull(initRpcResponse.Result, "Initialize 响应应有结果")
            Assert.AreEqual(CObj(1), initRpcResponse.Id, "响应ID应匹配请求ID")

            ' 验证初始化响应内容
            Dim resultDict = JsonConvert.DeserializeObject(Of Dictionary(Of String, Object))(initRpcResponse.Result.ToString())
            Assert.IsTrue(resultDict.ContainsKey("protocolVersion"), "响应应包含协议版本")
            Assert.IsTrue(resultDict.ContainsKey("capabilities"), "响应应包含服务器能力")
            Assert.IsTrue(resultDict.ContainsKey("serverInfo"), "响应应包含服务器信息")

            ' 2. 发送 initialized 通知（无 id）
            Dim initializedRequest = CreateJsonRpcRequest("initialized", Nothing, Nothing)
            Dim initializedContent As New StringContent(initializedRequest, Encoding.UTF8, "application/json")

            Dim initializedResponse = Await _httpClient.PostAsync(_baseUrl, initializedContent)
            ' initialized 是通知，应该成功但不返回内容
            Assert.IsTrue(initializedResponse.IsSuccessStatusCode, $"Initialized 通知失败，状态码: {initializedResponse.StatusCode}")

        Catch ex As HttpRequestException
            Assert.Fail($"初始化流程网络请求失败: {GetFullExceptionMessage(ex)}")
        Catch ex As JsonException
            Assert.Fail($"初始化响应JSON格式错误: {GetFullExceptionMessage(ex)}")
        Catch ex As Exception
            Assert.Fail($"初始化流程执行失败: {GetFullExceptionMessage(ex)}")
        End Try
    End Function

    ''' <summary>
    ''' 测试获取工具列表
    ''' </summary>
    <TestMethod>
    Public Async Function TestGetToolsListReal() As Task
        If Not IsServiceAvailable() Then
            Assert.Inconclusive($"MCP HTTP 服务未在端口 {DEFAULT_PORT} 运行，跳过集成测试")
            Return
        End If

        Try
            Dim toolsListRequest = CreateJsonRpcRequest("tools/list", Nothing, 2)
            Dim content As New StringContent(toolsListRequest, Encoding.UTF8, "application/json")

            Dim response = Await _httpClient.PostAsync(_baseUrl, content)
            Dim responseString = Await response.Content.ReadAsStringAsync()

            Assert.IsTrue(response.IsSuccessStatusCode, $"获取工具列表请求失败，状态码: {response.StatusCode}")

            Dim rpcResponse = JsonConvert.DeserializeObject(Of JsonRpcResponse)(responseString)
            Assert.IsNotNull(rpcResponse.Result, "工具列表响应应有结果")
            Assert.AreEqual(CObj(2), rpcResponse.Id, "响应ID应匹配请求ID")

            ' 验证返回的工具列表结构
            Dim resultDict = JsonConvert.DeserializeObject(Of Dictionary(Of String, Object))(rpcResponse.Result.ToString())
            Assert.IsTrue(resultDict.ContainsKey("tools"), "响应应包含tools字段")

            Dim toolsArray = JsonConvert.DeserializeObject(Of Object())(resultDict("tools").ToString())
            Assert.IsTrue(toolsArray.Length > 0, "应该返回至少一个工具")

        Catch ex As HttpRequestException
            Assert.Fail($"获取工具列表网络请求失败: {GetFullExceptionMessage(ex)}")
        Catch ex As JsonException
            Assert.Fail($"工具列表响应JSON格式错误: {GetFullExceptionMessage(ex)}")
        Catch ex As Exception
            Assert.Fail($"获取工具列表测试失败: {GetFullExceptionMessage(ex)}")
        End Try
    End Function

    ''' <summary>
    ''' 测试调用获取解决方案信息工具
    ''' </summary>
    <TestMethod>
    Public Async Function TestCallGetSolutionInfoReal() As Task
        If Not IsServiceAvailable() Then
            Assert.Inconclusive($"MCP HTTP 服务未在端口 {DEFAULT_PORT} 运行，跳过集成测试")
            Return
        End If

        Try
            Dim toolParams = New Dictionary(Of String, Object) From {
                {"name", "get_solution_info"},
                {"arguments", New Dictionary(Of String, Object)}
            }

            Dim toolCallRequest = CreateJsonRpcRequest("tools/call", toolParams, 3)
            Dim content As New StringContent(toolCallRequest, Encoding.UTF8, "application/json")

            Dim response = Await _httpClient.PostAsync(_baseUrl, content)
            Dim responseString = Await response.Content.ReadAsStringAsync()

            Assert.IsTrue(response.IsSuccessStatusCode, $"调用 get_solution_info 工具失败，状态码: {response.StatusCode}")

            Dim rpcResponse = JsonConvert.DeserializeObject(Of JsonRpcResponse)(responseString)
            Assert.IsNotNull(rpcResponse.Result, "工具调用响应应有结果")
            Assert.AreEqual(CObj(3), rpcResponse.Id, "响应ID应匹配请求ID")

            ' 验证工具响应结构
            Dim toolResponse = JsonConvert.DeserializeObject(Of Dictionary(Of String, Object))(rpcResponse.Result.ToString())
            Assert.IsTrue(toolResponse.ContainsKey("content"), "工具响应应包含content字段")
            Assert.IsTrue(toolResponse.ContainsKey("isError"), "工具响应应包含isError字段")

            Dim isError = CBool(toolResponse("isError"))
            Assert.IsFalse(isError, "get_solution_info 调用不应出错")

        Catch ex As HttpRequestException
            Assert.Fail($"调用 get_solution_info 网络请求失败: {GetFullExceptionMessage(ex)}")
        Catch ex As JsonException
            Assert.Fail($"get_solution_info 响应JSON格式错误: {GetFullExceptionMessage(ex)}")
        Catch ex As Exception
            Assert.Fail($"调用 get_solution_info 工具测试失败: {GetFullExceptionMessage(ex)}")
        End Try
    End Function

    ''' <summary>
    ''' 测试错误处理 - 无效方法
    ''' </summary>
    <TestMethod>
    Public Async Function TestInvalidMethodError() As Task
        If Not IsServiceAvailable() Then
            Assert.Inconclusive($"MCP HTTP 服务未在端口 {DEFAULT_PORT} 运行，跳过集成测试")
            Return
        End If

        Try
            Dim invalidRequest = CreateJsonRpcRequest("invalid_method", Nothing, 4)
            Dim content As New StringContent(invalidRequest, Encoding.UTF8, "application/json")

            Dim response = Await _httpClient.PostAsync(_baseUrl, content)
            Dim responseString = Await response.Content.ReadAsStringAsync()

            Assert.IsTrue(response.IsSuccessStatusCode, $"错误处理请求失败，状态码: {response.StatusCode}")

            Dim rpcResponse = JsonConvert.DeserializeObject(Of JsonRpcResponse)(responseString)
            Assert.IsNotNull(rpcResponse.Error, "应返回错误对象")
            Assert.AreEqual(-32601, rpcResponse.Error.Code, "应为Method not found错误")
            Assert.AreEqual("Method not found", rpcResponse.Error.Message, "错误消息应正确")
            Assert.AreEqual(CObj(4), rpcResponse.Id, "响应ID应匹配请求ID")

        Catch ex As HttpRequestException
            Assert.Fail($"错误处理网络请求失败: {GetFullExceptionMessage(ex)}")
        Catch ex As JsonException
            Assert.Fail($"错误处理响应JSON格式错误: {GetFullExceptionMessage(ex)}")
        Catch ex As Exception
            Assert.Fail($"错误处理测试失败: {GetFullExceptionMessage(ex)}")
        End Try
    End Function

    ''' <summary>
    ''' 测试无效 JSON 请求
    ''' </summary>
    <TestMethod>
    Public Async Function TestInvalidJsonError() As Task
        If Not IsServiceAvailable() Then
            Assert.Inconclusive($"MCP HTTP 服务未在端口 {DEFAULT_PORT} 运行，跳过集成测试")
            Return
        End If

        Try
            Dim invalidJson = "{ invalid json"
            Dim content As New StringContent(invalidJson, Encoding.UTF8, "application/json")

            Dim response = Await _httpClient.PostAsync(_baseUrl, content)
            Dim responseString = Await response.Content.ReadAsStringAsync()

            Assert.IsTrue(response.IsSuccessStatusCode, $"无效JSON请求失败，状态码: {response.StatusCode}")

            Dim rpcResponse = JsonConvert.DeserializeObject(Of JsonRpcResponse)(responseString)
            Assert.IsNotNull(rpcResponse.Error, "应返回错误对象")
            Assert.AreEqual(-32700, rpcResponse.Error.Code, "应为Parse error错误")

        Catch ex As HttpRequestException
            Assert.Fail($"无效JSON测试网络请求失败: {GetFullExceptionMessage(ex)}")
        Catch ex As JsonException
            Assert.Fail($"无效JSON测试响应JSON格式错误: {GetFullExceptionMessage(ex)}")
        Catch ex As Exception
            Assert.Fail($"无效JSON测试失败: {GetFullExceptionMessage(ex)}")
        End Try
    End Function

    ''' <summary>
    ''' 获取完整的异常信息，包括所有内部异常
    ''' </summary>
    Private Function GetFullExceptionMessage(ex As Exception) As String
        Dim message As New StringBuilder()
        message.AppendLine(ex.Message)

        Dim currentEx As Exception = ex
        Dim level As Integer = 1

        While currentEx.InnerException IsNot Nothing
            currentEx = currentEx.InnerException
            message.AppendLine($"  Inner exception {level}: {currentEx.Message}")
            level += 1
        End While

        ' 如果是 AggregateException，处理所有内部异常
        If TypeOf ex Is AggregateException Then
            Dim aggEx = CType(ex, AggregateException)
            message.AppendLine($"  AggregateException contains {aggEx.InnerExceptions.Count} exceptions:")

            For i = 0 To aggEx.InnerExceptions.Count - 1
                message.AppendLine($"    Exception {i + 1}: {aggEx.InnerExceptions(i).Message}")
            Next
        End If

        Return message.ToString().Trim()
    End Function

    ''' <summary>
    ''' 创建 JSON-RPC 请求的辅助方法
    ''' </summary>
    Private Function CreateJsonRpcRequest(method As String, params As Object, id As Object) As String
        Dim request As New Dictionary(Of String, Object) From {
            {"jsonrpc", "2.0"},
            {"method", method}
        }

        If params IsNot Nothing Then
            request("params") = params
        End If

        If id IsNot Nothing Then
            request("id") = id
        End If

        Return JsonConvert.SerializeObject(request, Formatting.None)
    End Function
End Class
