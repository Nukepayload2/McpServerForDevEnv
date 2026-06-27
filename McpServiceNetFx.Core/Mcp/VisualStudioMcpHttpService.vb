Imports Newtonsoft.Json

' MCP HTTP 服务 - 纯逻辑类，由 McpService 通过 HttpListener 调用
Public Class VisualStudioMcpHttpService
    Private ReadOnly _toolManager As VisualStudioToolManager

    Sub New(toolManager As VisualStudioToolManager)
        _toolManager = toolManager
    End Sub

    Public Async Function ProcessMcpRequest(request As JsonRpcRequest) As Task(Of JsonRpcResponse)
        Try
            ' 直接处理请求，JSON序列化由自定义格式化器处理
            Dim response As JsonRpcResponse = Await ProcessRequest(request)

            ' 对于通知（没有id），返回null，格式化器会处理为NoContent响应
            Return response
        Catch ex As Exception
            Return CreateErrorResponse(-32700, "Parse error", If(request?.Id, Nothing), ex.Message)
        End Try
    End Function

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
            Return New Dictionary(Of String, Object) From {
                {"error", ex.Message}
            }
        End Try
    End Function

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

        ' 如果是通知（没有id），不应该发送响应
        If request.Id Is Nothing AndAlso response IsNot Nothing Then
            Return Nothing
        End If

        Return response
    End Function

    Private Function ProcessInitialize(request As JsonRpcRequest) As JsonRpcResponse
        Try
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

            If TypeOf toolResult Is CallToolErrorResult Then
                Dim errorResult = CType(toolResult, CallToolErrorResult)
                Return CreateErrorResponse(-32603, errorResult.ErrorMessage, request.Id)
            End If

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
            If Not _toolManager.HasTool(params.Name) Then
                Return New CallToolErrorResult($"Unknown tool: {params.Name}")
            End If

            Dim result = Await _toolManager.ExecuteToolAsync(params.Name, If(params.Arguments, New Dictionary(Of String, Object)()))

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
