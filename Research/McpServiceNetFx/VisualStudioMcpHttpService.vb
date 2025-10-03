Imports System.ServiceModel
Imports System.ServiceModel.Web
Imports System.ServiceModel.Channels
Imports Newtonsoft.Json
Imports System.Windows.Threading
Imports System.IO
Imports System.ServiceModel.Activation
Imports System.Diagnostics
Imports System.Text

' JSON-RPC 2.0 数据模型 - 使用 Newtonsoft.Json 序列化
Public Class JsonRpcRequest
    <JsonProperty("jsonrpc")>
    Public Property JsonRpc As String = "2.0"

    <JsonProperty("method")>
    Public Property Method As String

    <JsonProperty("params", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Params As Object

    <JsonProperty("id", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Id As Object
End Class

Public Class JsonRpcResponse
    <JsonProperty("jsonrpc", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property JsonRpc As String = "2.0"

    <JsonProperty("result", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Result As Object

    <JsonProperty("error", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property [Error] As JsonRpcError

    <JsonProperty("id", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Id As Object
End Class

Public Class JsonRpcError
    <JsonProperty("code")>
    Public Property Code As Integer

    <JsonProperty("message")>
    Public Property Message As String

    <JsonProperty("data", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Data As Object
End Class

' MCP HTTP 服务契约 - 使用 Stream 自定义数据交互，完全控制 JSON 序列化和反序列化
<ServiceContract>
<AspNetCompatibilityRequirements(RequirementsMode:=AspNetCompatibilityRequirementsMode.Allowed)>
<ServiceBehavior(InstanceContextMode:=InstanceContextMode.Single, ConcurrencyMode:=ConcurrencyMode.Multiple, Namespace:="")>
Public Class VisualStudioMcpHttpService
    Private ReadOnly _visualStudioMcpTools As VisualStudioMcpTools

    Sub New(vs As VisualStudioMcpTools)
        _visualStudioMcpTools = vs
    End Sub

    ' 处理 MCP 请求 - 使用对象参数配合自定义JSON格式化器
    <WebInvoke(UriTemplate:="", Method:="POST", BodyStyle:=WebMessageBodyStyle.Bare)>
    Public Function ProcessMcpRequest(request As JsonRpcRequest) As JsonRpcResponse
        Try
            ' 直接处理请求，JSON序列化由自定义格式化器处理
            Dim response As JsonRpcResponse = ProcessRequest(request)

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
    Private Function ProcessRequest(request As JsonRpcRequest) As JsonRpcResponse
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
            Case "initialized"
                response = ProcessInitialized(request)
            Case "tools/list"
                response = ProcessToolsList(request.Id)
            Case "tools/call"
                response = ProcessToolsCall(request)
            Case "ping"
                response = ProcessPing(request.Id)
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
            ' 解析客户端初始化参数 - 符合 MCP 初始化规范
            Dim clientParams As Object = Nothing
            If request.Params IsNot Nothing AndAlso Not (TypeOf request.Params Is String AndAlso String.IsNullOrEmpty(request.Params.ToString())) Then
                ' 检查是否是空对象 {}
                Dim paramStr = request.Params.ToString()
                If paramStr <> "System.Object" AndAlso paramStr <> "" Then
                    Try
                        clientParams = JsonConvert.DeserializeObject(Of Dictionary(Of String, Object))(paramStr)
                    Catch
                        ' 如果解析失败，保留原始参数
                        clientParams = request.Params
                    End Try
                End If
            End If

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

    Private Function ProcessToolsCall(request As JsonRpcRequest) As JsonRpcResponse
        Try
            ' 符合 MCP tools/call 规范
            If request.Params Is Nothing Then
                Return CreateErrorResponse(-32602, "Invalid params", request.Id, "Tool call requires name and arguments")
            End If

            Dim result = HandleToolCall(request.Params).GetAwaiter().GetResult()
            Return CreateSuccessResponse(result, request.Id)
        Catch ex As Exception
            Return CreateErrorResponse(-32603, "Internal error", request.Id, ex.Message)
        End Try
    End Function

    Private Function ProcessPing(id As Object) As JsonRpcResponse
        ' MCP ping 协议实现 - 符合 MCP 规范，简单返回空结果
        Return CreateSuccessResponse(New Dictionary(Of String, Object)(), id)
    End Function

    Private Function GetToolsList() As ToolsListResponse
        Dim tools As New List(Of ToolDefinition) From {
            New ToolDefinition With {
                .Name = "build_solution",
                .Description = "构建整个解决方案",
                .InputSchema = New InputSchema With {
                    .Type = "object",
                    .Properties = New Dictionary(Of String, PropertyDefinition) From {
                        {"configuration", New PropertyDefinition With {
                            .Type = "string",
                            .Description = "构建配置 (Debug/Release)",
                            .[Default] = "Debug"
                        }}
                    }
                }
            },
            New ToolDefinition With {
                .Name = "build_project",
                .Description = "构建指定项目",
                .InputSchema = New InputSchema With {
                    .Type = "object",
                    .Properties = New Dictionary(Of String, PropertyDefinition) From {
                        {"projectName", New PropertyDefinition With {
                            .Type = "string",
                            .Description = "项目名称"
                        }},
                        {"configuration", New PropertyDefinition With {
                            .Type = "string",
                            .Description = "构建配置 (Debug/Release)",
                            .[Default] = "Debug"
                        }}
                    },
                    .Required = {"projectName"}
                }
            },
            New ToolDefinition With {
                .Name = "get_error_list",
                .Description = "获取当前的错误和警告列表",
                .InputSchema = New InputSchema With {
                    .Type = "object",
                    .Properties = New Dictionary(Of String, PropertyDefinition) From {
                        {"severity", New PropertyDefinition With {
                            .Type = "string",
                            .Description = "过滤级别 (Error/Warning/Message/All)",
                            .[Default] = "All"
                        }}
                    }
                }
            },
            New ToolDefinition With {
                .Name = "get_solution_info",
                .Description = "获取当前解决方案信息",
                .InputSchema = New InputSchema With {
                    .Type = "object",
                    .Properties = New Dictionary(Of String, PropertyDefinition)()
                }
            }
        }

        Return New ToolsListResponse With {
            .Tools = tools.ToArray()
        }
    End Function

    Private Async Function HandleToolCall(params As Object) As Task(Of Object)
        Try
            ' 解析工具调用参数
            Dim toolParams = JsonConvert.DeserializeObject(Of ToolCallParams)(params.ToString())
            Dim content As New List(Of McpContentItem)

            Select Case toolParams.name
                Case "build_solution"
                    Dim result = Await _visualStudioMcpTools.BuildSolution(If(toolParams.arguments?.ContainsKey("configuration"), toolParams.arguments("configuration").ToString(), "Debug"))

                    Dim buildText As String = $"Build {(If(result.Success, "succeeded", "failed"))}" & vbCrLf &
                                          $"Configuration: {result.Configuration}" & vbCrLf &
                                          $"Build Time: {result.BuildTime.TotalSeconds:F2}s" & vbCrLf &
                                          $"Message: {result.Message}"

                    If result.Errors?.Any() Then
                        buildText &= vbCrLf & vbCrLf & "Errors:" & vbCrLf & String.Join(vbCrLf, result.Errors.Select(Function(e) $"  - {e.Message}"))
                    End If

                    If result.Warnings?.Any() Then
                        buildText &= vbCrLf & vbCrLf & "Warnings:" & vbCrLf & String.Join(vbCrLf, result.Warnings.Select(Function(w) $"  - {w.Message}"))
                    End If

                    content.Add(New McpContentItem("text", buildText))
                    Return New McpToolResponse(content, Not result.Success)

                Case "build_project"
                    Dim result = Await _visualStudioMcpTools.BuildProject(
                        toolParams.arguments("projectName").ToString(),
                        If(toolParams.arguments?.ContainsKey("configuration"), toolParams.arguments("configuration").ToString(), "Debug"))

                    Dim buildText As String = $"Project Build {(If(result.Success, "succeeded", "failed"))}" & vbCrLf &
                                          $"Project: {toolParams.arguments("projectName").ToString()}" & vbCrLf &
                                          $"Configuration: {result.Configuration}" & vbCrLf &
                                          $"Build Time: {result.BuildTime.TotalSeconds:F2}s" & vbCrLf &
                                          $"Message: {result.Message}"

                    If result.Errors?.Any() Then
                        buildText &= vbCrLf & vbCrLf & "Errors:" & vbCrLf & String.Join(vbCrLf, result.Errors.Select(Function(e) $"  - {e.Message}"))
                    End If

                    If result.Warnings?.Any() Then
                        buildText &= vbCrLf & vbCrLf & "Warnings:" & vbCrLf & String.Join(vbCrLf, result.Warnings.Select(Function(w) $"  - {w.Message}"))
                    End If

                    content.Add(New McpContentItem("text", buildText))
                    Return New McpToolResponse(content, Not result.Success)

                Case "get_error_list"
                    Dim errorList = _visualStudioMcpTools.GetErrorList(If(toolParams.arguments?.ContainsKey("severity"), toolParams.arguments("severity").ToString(), "All"))

                    Dim errorText As String = $"Error List ({errorList.TotalCount} items)" & vbCrLf

                    If errorList.Errors?.Any() Then
                        errorText &= vbCrLf & "Errors:" & vbCrLf & String.Join(vbCrLf, errorList.Errors.Select(Function(e) $"  - [{e.Severity}] {e.Message} (Line {e.Line})"))
                    End If

                    If errorList.Warnings?.Any() Then
                        errorText &= vbCrLf & "Warnings:" & vbCrLf & String.Join(vbCrLf, errorList.Warnings.Select(Function(w) $"  - [{w.Severity}] {w.Message} (Line {w.Line})"))
                    End If

                    content.Add(New McpContentItem("text", errorText))
                    Return New McpToolResponse(content)

                Case "get_solution_info"
                    Dim solutionInfo = _visualStudioMcpTools.GetSolutionInfo()

                    Dim infoText As String = $"Solution Information:" & vbCrLf &
                                          $"Name: {solutionInfo.Name}" & vbCrLf &
                                          $"Full Name: {solutionInfo.FullName}" & vbCrLf &
                                          $"Projects Count: {solutionInfo.Count}" & vbCrLf &
                                          $"Active Configuration: {solutionInfo.ActiveConfiguration.ConfigurationName} | {solutionInfo.ActiveConfiguration.PlatformName}"

                    If solutionInfo.Projects?.Any() Then
                        infoText &= vbCrLf & vbCrLf & "Projects:" & vbCrLf & String.Join(vbCrLf, solutionInfo.Projects.Select(Function(p) $"  - {p.Name} ({p.Kind})"))
                    End If

                    content.Add(New McpContentItem("text", infoText))
                    Return New McpToolResponse(content)

                Case Else
                    content.Add(New McpContentItem("text", $"Unknown tool: {toolParams.name}"))
                    Return New McpToolResponse(content, True)
            End Select

        Catch ex As Exception
            Dim content = New List(Of McpContentItem) From {
                New McpContentItem("text", $"Tool call failed: {ex.Message}")
            }
            Return New McpToolResponse(content, True)
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

' MCP 工具定义的显式类型
Public Class ToolsListResponse
    Public Property Tools As ToolDefinition()
End Class

' 构建结果强类型
Public Class BuildResultResponse
    Public Property Success As Boolean
    Public Property Message As String
    Public Property BuildTime As TimeSpan
    Public Property Configuration As String
    Public Property Errors As CompilationError()
    Public Property Warnings As CompilationError()
End Class

' 解决方案信息强类型
Public Class SolutionInfoResponse
    Public Property FullName As String
    Public Property Name As String
    Public Property Count As Integer
    Public Property Projects As ProjectInfo()
    Public Property ActiveConfiguration As ConfigurationInfo
End Class

Public Class ProjectInfo
    Public Property Name As String
    Public Property FullName As String
    Public Property UniqueName As String
    Public Property Kind As String
End Class

Public Class ConfigurationInfo
    Public Property Name As String
    Public Property ConfigurationName As String
    Public Property PlatformName As String
End Class

' 错误列表强类型
Public Class ErrorListResponse
    Public Property Errors As CompilationError()
    Public Property Warnings As CompilationError()
    Public Property TotalCount As Integer
End Class

Public Class ToolDefinition
    Public Property Name As String
    Public Property Description As String
    Public Property InputSchema As InputSchema
End Class

Public Class InputSchema
    Public Property Type As String
    Public Property Properties As Dictionary(Of String, PropertyDefinition)
    Public Property Required As String()
End Class

Public Class PropertyDefinition
    Public Property Type As String
    Public Property Description As String
    Public Property [Default] As String
End Class

Public Class ToolCallParams
    Public Property name As String
    Public Property arguments As Dictionary(Of String, Object)
End Class

' MCP 标准内容响应类
Public Class McpContentItem
    <JsonProperty("type", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Type As String

    <JsonProperty("text", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Text As String

    <JsonProperty("data", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Data As String

    <JsonProperty("mimeType", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property MimeType As String

    Public Sub New(type As String, text As String)
        Me.Type = type
        Me.Text = text
    End Sub

    Public Sub New(type As String, data As String, mimeType As String)
        Me.Type = type
        Me.Data = data
        Me.MimeType = mimeType
    End Sub
End Class

Public Class McpToolResponse
    <JsonProperty("content", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Content As List(Of McpContentItem)

    <JsonProperty("isError", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property IsError As Boolean

    Public Sub New(content As List(Of McpContentItem), Optional isError As Boolean = False)
        Me.Content = content
        Me.IsError = isError
    End Sub
End Class

' MCP 进度通知类
Public Class McpProgressNotification
    <JsonProperty("progressToken")>
    Public Property ProgressToken As String

    <JsonProperty("progress", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Progress As Double

    <JsonProperty("total", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Total As Double

    <JsonProperty("message", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Message As String

    Public Sub New(progressToken As String, Optional progress As Double = 0, Optional total As Double = 0, Optional message As String = "")
        Me.ProgressToken = progressToken
        Me.Progress = progress
        Me.Total = total
        Me.Message = message
    End Sub
End Class