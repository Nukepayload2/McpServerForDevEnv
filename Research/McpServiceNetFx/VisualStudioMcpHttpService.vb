Imports System.ServiceModel
Imports System.ServiceModel.Web
Imports Newtonsoft.Json
Imports System.Windows.Threading

<ServiceContract>
Public Interface IMcpHttpService
    <OperationContract>
    <WebInvoke(Method:="POST", RequestFormat:=WebMessageFormat.Json, ResponseFormat:=WebMessageFormat.Json, BodyStyle:=WebMessageBodyStyle.Bare)>
    Function ProcessRequest(request As String) As String
End Interface

Public Class VisualStudioMcpHttpService
    Implements IMcpHttpService

    Private ReadOnly _visualStudioMcpTools As VisualStudioMcpTools

    Sub New(vs As VisualStudioMcpTools)
        _visualStudioMcpTools = vs
    End Sub

    Public Function ProcessRequest(request As String) As String Implements IMcpHttpService.ProcessRequest
        Try
            ' 解析 JSON-RPC 请求
            Dim rpcRequest = JsonConvert.DeserializeObject(Of JsonRpcRequest)(request)

            ' 处理 MCP 请求
            Dim result = ProcessMcpRequest(rpcRequest).GetAwaiter().GetResult()

            ' 返回 JSON-RPC 响应
            Dim response = New JsonRpcResponse With {
                .jsonrpc = "2.0",
                .id = rpcRequest.id,
                .result = result
            }

            Return JsonConvert.SerializeObject(response)

        Catch ex As Exception
            ' 返回错误响应
            Dim errorResponse = New JsonRpcResponse With {
                .jsonrpc = "2.0",
                .id = Nothing,
                .[error] = New JsonRpcError With {
                    .code = -32603,
                    .message = "Internal error",
                    .data = ex.Message
                }
            }

            Return JsonConvert.SerializeObject(errorResponse)
        End Try
    End Function

    Private Async Function ProcessMcpRequest(request As JsonRpcRequest) As Task(Of Object)
        ' 根据方法名调用相应的 MCP 工具
        Select Case request.method
            Case "tools/call"
                Return Await HandleToolCall(request.params)
            Case "tools/list"
                Return HandleToolsList()
            Case Else
                Throw New ArgumentException($"Unknown method: {request.method}")
        End Select
    End Function

    Private Async Function HandleToolCall(params As Object) As Task(Of Object)
        Try
            ' 解析工具调用参数
            Dim toolParams = JsonConvert.DeserializeObject(Of ToolCallParams)(params.ToString())

            Select Case toolParams.name
                Case "build_solution"
                    Dim result = Await _visualStudioMcpTools.BuildSolution(If(toolParams.arguments?.ContainsKey("configuration"), toolParams.arguments("configuration").ToString(), "Debug"))
                    Return New BuildResultResponse With {
                        .Success = result.Success,
                        .Message = result.Message,
                        .BuildTime = result.BuildTime,
                        .Configuration = result.Configuration,
                        .Errors = result.Errors?.ToArray(),
                        .Warnings = result.Warnings?.ToArray()
                    }
                Case "build_project"
                    Dim result = Await _visualStudioMcpTools.BuildProject(
                        toolParams.arguments("projectName").ToString(),
                        If(toolParams.arguments?.ContainsKey("configuration"), toolParams.arguments("configuration").ToString(), "Debug"))
                    Return New BuildResultResponse With {
                        .Success = result.Success,
                        .Message = result.Message,
                        .BuildTime = result.BuildTime,
                        .Configuration = result.Configuration,
                        .Errors = result.Errors?.ToArray(),
                        .Warnings = result.Warnings?.ToArray()
                    }
                Case "get_error_list"
                    Return _visualStudioMcpTools.GetErrorList(If(toolParams.arguments?.ContainsKey("severity"), toolParams.arguments("severity").ToString(), "All"))
                Case "get_solution_info"
                    Return _visualStudioMcpTools.GetSolutionInfo()
                Case Else
                    Throw New ArgumentException($"Unknown tool: {toolParams.name}")
            End Select

        Catch ex As Exception
            Throw New Exception($"Tool call failed: {ex.Message}", ex)
        End Try
    End Function

    Private Function HandleToolsList() As ToolsListResponse
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

' JSON-RPC 请求和响应模型
Public Class JsonRpcRequest
    Public Property jsonrpc As String = "2.0"
    Public Property method As String
    Public Property params As Object
    Public Property id As Object
End Class

Public Class JsonRpcResponse
    Public Property jsonrpc As String = "2.0"
    Public Property id As Object
    Public Property result As Object
    Public Property [error] As JsonRpcError
End Class

Public Class JsonRpcError
    Public Property code As Integer
    Public Property message As String
    Public Property data As Object
End Class

Public Class ToolCallParams
    Public Property name As String
    Public Property arguments As Dictionary(Of String, Object)
End Class