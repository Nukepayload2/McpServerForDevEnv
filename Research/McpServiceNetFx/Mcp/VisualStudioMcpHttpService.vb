Imports System.ServiceModel
Imports System.ServiceModel.Web
Imports Newtonsoft.Json
Imports System.ServiceModel.Activation

' MCP HTTP 服务契约 - 使用 Stream 自定义数据交互，完全控制 JSON 序列化和反序列化
<ServiceContract>
<AspNetCompatibilityRequirements(RequirementsMode:=AspNetCompatibilityRequirementsMode.Allowed)>
<ServiceBehavior(InstanceContextMode:=InstanceContextMode.Single, ConcurrencyMode:=ConcurrencyMode.Multiple, Namespace:="")>
Public Class VisualStudioMcpHttpService
    Private ReadOnly _toolManager As VisualStudioToolManager

    Sub New(toolManager As VisualStudioToolManager)
        _toolManager = toolManager
    End Sub

    ' 处理 MCP 请求 - 使用对象参数配合自定义JSON格式化器
    <WebInvoke(UriTemplate:="", Method:="POST", BodyStyle:=WebMessageBodyStyle.Bare)>
    Public Async Function ProcessMcpRequest(request As JsonRpcRequest) As Task(Of JsonRpcResponse)
        Try
            ' 直接处理请求，JSON序列化由自定义格式化器处理
            Dim response As JsonRpcResponse = Await ProcessRequest(request)

            ' 对于通知（没有id），返回null，格式化器会处理为NoContent响应
            Return response
        Catch ex As Exception
            ' 错误处理
            Return CreateErrorResponse(-32700, "Parse error", If(request?.Id, Nothing), ex.Message)
        End Try
    End Function

    ' 获取服务状态 - 简单的健康检查端点
    <WebGet(UriTemplate:="status")>
    Public Function GetStatus() As Dictionary(Of String, Object)
        Try
            Dim statusInfo = New Dictionary(Of String, Object) From {
                {"status", "running"},
                {"service", "Visual Studio MCP Server"},
                {"version", "1.0.0"},
                {"timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}
            }

            ' 直接返回对象，JSON序列化由自定义格式化器处理
            Return statusInfo
        Catch ex As Exception
            ' 返回错误信息
            Return New Dictionary(Of String, Object) From {
                {"error", ex.Message}
            }
        End Try
    End Function

    ' 处理 JSON-RPC 请求的核心逻辑
    Private Async Function ProcessRequest(request As JsonRpcRequest) As Task(Of JsonRpcResponse)
        If request Is Nothing Then
            Return CreateErrorResponse(-32700, "Parse error", Nothing)
        End If

        If request.JsonRpc <> "2.0" Then
            Return CreateErrorResponse(-32600, "Invalid Request", request.Id, "JSON RPC version must be 2.0")
        End If

        Dim response As JsonRpcResponse = Nothing

        Select Case request.Method
            Case "initialize"
                response = ProcessInitialize(request)
            Case "notifications/initialized"
                response = ProcessInitialized(request)
            Case "tools/list"
                response = ProcessToolsList(request.Id)
            Case "tools/call"
                response = Await ProcessToolsCall(request)
            Case "ping"
                response = ProcessPing(request.Id)
            Case "notifications/canceled", "prompts/list", "resources/list"
                ' Reserved
            Case Else
                response = CreateErrorResponse(-32601, "Method not found", request.Id, $"Method '{request.Method}' is not supported by this MCP server")
        End Select

        ' 记录工具调用日志（通过工具管理器内部处理）

        ' 如果是通知（没有id），不应该发送响应
        If request.Id Is Nothing AndAlso response IsNot Nothing Then
            Return Nothing
        End If

        Return response
    End Function

    Private Function ProcessInitialize(request As JsonRpcRequest) As JsonRpcResponse
        Try
            ' 构建服务器初始化响应 - 符合 MCP 规范
            Dim serverCapabilities = New Dictionary(Of String, Object) From {
                {"tools", New Dictionary(Of String, Object) From {
                    {"listChanged", False}
                }}
            }

            Dim serverInfo = New Dictionary(Of String, Object) From {
                {"name", "Visual Studio MCP Server"},
                {"version", "1.0.0"}
            }

            Dim result = New Dictionary(Of String, Object) From {
                {"protocolVersion", "2024-10-07"},
                {"capabilities", serverCapabilities},
                {"serverInfo", serverInfo},
                {"instructions", "This server provides Visual Studio integration tools for building solutions, projects, and getting error information."}
            }

            Return CreateSuccessResponse(result, request.Id)
        Catch ex As Exception
            Return CreateErrorResponse(-32603, "Internal error", request.Id, ex.Message)
        End Try
    End Function

    Private Function ProcessInitialized(request As JsonRpcRequest) As JsonRpcResponse
        ' 根据 MCP 规范，initialized 通知应该没有id字段，是纯通知
        ' 如果有id，说明是无效的请求格式
        If request.Id IsNot Nothing Then
            Return CreateErrorResponse(-32600, "Invalid Request", request.Id, "notifications/initialized should not have an id")
        End If

        ' 记录客户端已初始化完成，可以进行正常的工具调用
        ' 不返回响应，因为这是通知
        Return Nothing
    End Function

    Private Function ProcessToolsList(id As Object) As JsonRpcResponse
        Try
            ' 符合 MCP tools/list 规范
            Dim tools = GetToolsList()
            Return CreateSuccessResponse(tools, id)
        Catch ex As Exception
            Return CreateErrorResponse(-32603, "Internal error", id, ex.Message)
        End Try
    End Function

    Private Async Function ProcessToolsCall(request As JsonRpcRequest) As Task(Of JsonRpcResponse)
        Try
            ' 符合 MCP tools/call 规范
            If request.Params Is Nothing Then
                Return CreateErrorResponse(-32602, "Invalid params", request.Id, "Tool call requires name and arguments")
            End If

            Dim toolResult = Await HandleToolCall(request.Params)

            ' 检查工具调用结果类型
            If TypeOf toolResult Is CallToolErrorResult Then
                Dim errorResult = CType(toolResult, CallToolErrorResult)
                Return CreateErrorResponse(-32603, "Tool execution failed", request.Id, errorResult.ErrorMessage)
            End If

            ' 返回成功结果
            Return CreateSuccessResponse(toolResult, request.Id)
        Catch ex As Exception
            Return CreateErrorResponse(-32603, "Internal error", request.Id, ex.Message)
        End Try
    End Function

    Private Function ProcessPing(id As Object) As JsonRpcResponse
        ' MCP ping 协议实现 - 符合 MCP 规范，简单返回空结果
        Return CreateSuccessResponse(New Dictionary(Of String, Object)(), id)
    End Function

    Private Function GetToolsList() As ToolsListResponse
        Try
            Dim tools = _toolManager.GetToolDefinitions()
            Return New ToolsListResponse With {
                .Tools = tools
            }
        Catch ex As Exception
            ' 如果获取工具列表失败，返回空列表
            Return New ToolsListResponse With {
                .Tools = New ToolDefinition() {}
            }
        End Try
    End Function

    Private Async Function HandleToolCall(params As ToolCallParams) As Task(Of CallToolResultBase)
        Try
            ' 检查工具是否存在
            If Not _toolManager.HasTool(params.Name) Then
                Return New CallToolErrorResult($"Unknown tool: {params.Name}")
            End If

            ' 执行工具
            Dim result = Await _toolManager.ExecuteToolAsync(params.Name, If(params.Arguments, New Dictionary(Of String, Object)()))

            ' 创建成功结果，包含结构化内容
            Dim successResult = New CallToolSuccessResult With {
                .StructuredContent = result
            }

            ' 如果结果可以转换为文本，也添加文本内容
            If result IsNot Nothing Then
                Dim jsonResult = JsonConvert.SerializeObject(result, Formatting.Indented)
                successResult.Content.Add(New TextContentBlock With {.Text = jsonResult})
            End If

            Return successResult
        Catch ex As Exception
            ' 返回错误结果
            Return New CallToolErrorResult($"Tool call failed: {ex.Message}")
        End Try
    End Function

    Private Function CreateSuccessResponse(result As Object, id As Object) As JsonRpcResponse
        Return New JsonRpcResponse With {
            .JsonRpc = "2.0",
            .Result = result,
            .Id = id
        }
    End Function

    Private Function CreateErrorResponse(code As Integer, message As String, id As Object, Optional data As Object = Nothing) As JsonRpcResponse
        Return New JsonRpcResponse With {
            .JsonRpc = "2.0",
            .Error = New JsonRpcError With {
                .Code = code,
                .Message = message,
                .Data = data
            },
            .Id = id
        }
    End Function
End Class
