Imports System.ServiceModel
Imports System.ServiceModel.Web
Imports Newtonsoft.Json
Imports System.ServiceModel.Activation
Imports System.Runtime.CompilerServices

' JSON-RPC 2.0 数据模型 - 使用 Newtonsoft.Json 序列化
Public Class JsonRpcRequest
    <JsonProperty("jsonrpc")>
    Public Property JsonRpc As String = "2.0"

    <JsonProperty("method")>
    Public Property Method As String

    <JsonProperty("params", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Params As ToolCallParams

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

' MCP 工具定义的显式类型
Public Class ToolsListResponse
    <JsonProperty("tools")>
    Public Property Tools As ToolDefinition()
End Class

' 构建结果强类型
Public Class BuildResultResponse
    <JsonProperty("success")>
    Public Property Success As Boolean

    <JsonProperty("message")>
    Public Property Message As String

    <JsonProperty("buildTime")>
    Public Property BuildTime As TimeSpan

    <JsonProperty("configuration")>
    Public Property Configuration As String

    <JsonProperty("errors", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Errors As CompilationError()

    <JsonProperty("warnings", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Warnings As CompilationError()
End Class

' 解决方案信息强类型
Public Class SolutionInfoResponse
    <JsonProperty("fullName")>
    Public Property FullName As String

    <JsonProperty("count")>
    Public Property Count As Integer

    <JsonProperty("projects")>
    Public Property Projects As ProjectInfo()

    <JsonProperty("activeConfiguration")>
    Public Property ActiveConfiguration As ConfigurationInfo
End Class

Public Class ProjectInfo
    <JsonProperty("name")>
    Public Property Name As String

    <JsonProperty("fullName")>
    Public Property FullName As String

    <JsonProperty("uniqueName")>
    Public Property UniqueName As String

    <JsonProperty("kind")>
    Public Property Kind As String
End Class

Public Class ConfigurationInfo
    <JsonProperty("name")>
    Public Property Name As String

    <JsonProperty("configurationName")>
    Public Property ConfigurationName As String

    <JsonProperty("platformName")>
    Public Property PlatformName As String
End Class

' 错误列表强类型
Public Class ErrorListResponse
    <JsonProperty("errors", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Errors As CompilationError()

    <JsonProperty("warnings", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Warnings As CompilationError()

    <JsonProperty("totalCount")>
    Public Property TotalCount As Integer
End Class

' 活动文档响应强类型
Public Class ActiveDocumentResponse
    <JsonProperty("path", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Path As String

    <JsonProperty("hasActiveDocument")>
    Public Property HasActiveDocument As Boolean
End Class

Public Class ToolDefinition
    <JsonProperty("name")>
    Public Property Name As String

    <JsonProperty("description")>
    Public Property Description As String

    <JsonProperty("inputSchema")>
    Public Property InputSchema As InputSchema
End Class

Public Class InputSchema
    <JsonProperty("type")>
    Public Property Type As String

    <JsonProperty("properties")>
    Public Property Properties As Dictionary(Of String, PropertyDefinition)

    <JsonProperty("required", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Required As String()
End Class

Public Class PropertyDefinition
    <JsonProperty("type")>
    Public Property Type As String

    <JsonProperty("description")>
    Public Property Description As String

    <JsonProperty("default", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property [Default] As String
End Class

Public Class ToolCallParams
    <JsonProperty("name")>
    Public Property Name As String

    <JsonProperty("arguments", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Arguments As Dictionary(Of String, Object)
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

''' <summary>
''' 表示 MCP 工具调用成功结果的基类
''' </summary>
Public MustInherit Class CallToolResultBase
    ''' <summary>
    ''' 获取或设置响应内容列表
    ''' </summary>
    <JsonProperty("content")>
    Public Property Content As IList(Of ContentBlock) = New List(Of ContentBlock)()

    ''' <summary>
    ''' 获取或设置可选的结构化结果
    ''' </summary>
    <JsonProperty("structuredContent")>
    Public Property StructuredContent As Object
End Class

''' <summary>
''' 表示 MCP 工具调用成功结果
''' </summary>
''' <remarks>
''' 工具成功执行后返回的结果，包含工具的输出内容
''' </remarks>
Public Class CallToolSuccessResult
    Inherits CallToolResultBase

    ''' <summary>
    ''' 初始化 CallToolSuccessResult 的新实例
    ''' </summary>
    Public Sub New()
        MyBase.New()
    End Sub

    ''' <summary>
    ''' 使用指定的内容初始化 CallToolSuccessResult 的新实例
    ''' </summary>
    ''' <param name="content">工具返回的内容</param>
    Public Sub New(content As IList(Of ContentBlock))
        Me.New()
        Me.Content = content
    End Sub

    ''' <summary>
    ''' 使用指定的文本内容初始化 CallToolSuccessResult 的新实例
    ''' </summary>
    ''' <param name="text">工具返回的文本内容</param>
    Public Sub New(text As String)
        Me.New()
        If Not String.IsNullOrEmpty(text) Then
            Content.Add(New TextContentBlock With {.Text = text})
        End If
    End Sub
End Class

''' <summary>
''' 表示 MCP 工具调用错误结果
''' </summary>
''' <remarks>
''' 工具执行失败时返回的错误结果，包含错误信息和详细信息
''' </remarks>
Public Class CallToolErrorResult
    Inherits CallToolResultBase

    ''' <summary>
    ''' 获取或设置错误指示器，始终为 true
    ''' </summary>
    <JsonProperty("isError")>
    Public Property IsError As Boolean = True

    ''' <summary>
    ''' 获取或设置错误消息
    ''' </summary>
    <JsonProperty("errorMessage")>
    Public Property ErrorMessage As String

    ''' <summary>
    ''' 获取或设置错误代码
    ''' </summary>
    <JsonProperty("errorCode")>
    Public Property ErrorCode As String

    ''' <summary>
    ''' 获取或设置错误详情
    ''' </summary>
    <JsonProperty("errorDetails")>
    Public Property ErrorDetails As Object

    ''' <summary>
    ''' 初始化 CallToolErrorResult 的新实例
    ''' </summary>
    Public Sub New()
        MyBase.New()
    End Sub

    ''' <summary>
    ''' 使用指定的错误消息初始化 CallToolErrorResult 的新实例
    ''' </summary>
    ''' <param name="errorMessage">错误消息</param>
    Public Sub New(errorMessage As String)
        Me.New()
        Me.ErrorMessage = errorMessage
    End Sub

    ''' <summary>
    ''' 使用指定的错误消息和错误代码初始化 CallToolErrorResult 的新实例
    ''' </summary>
    ''' <param name="errorMessage">错误消息</param>
    ''' <param name="errorCode">错误代码</param>
    Public Sub New(errorMessage As String, errorCode As String)
        Me.New(errorMessage)
        Me.ErrorCode = errorCode
    End Sub

    ''' <summary>
    ''' 使用指定的错误消息、错误代码和错误详情初始化 CallToolErrorResult 的新实例
    ''' </summary>
    ''' <param name="errorMessage">错误消息</param>
    ''' <param name="errorCode">错误代码</param>
    ''' <param name="errorDetails">错误详情</param>
    Public Sub New(errorMessage As String, errorCode As String, errorDetails As Object)
        Me.New(errorMessage, errorCode)
        Me.ErrorDetails = errorDetails
    End Sub
End Class

''' <summary>
''' 内容块基类
''' </summary>
Public MustInherit Class ContentBlock
    ''' <summary>
    ''' 获取或设置内容类型
    ''' </summary>
    <JsonProperty("type")>
    Public Property Type As String = String.Empty

    ''' <summary>
    ''' 获取或设置可选的注释
    ''' </summary>
    <JsonProperty("annotations")>
    Public Property Annotations As Object
End Class

''' <summary>
''' 文本内容块
''' </summary>
Public Class TextContentBlock
    Inherits ContentBlock

    ''' <summary>
    ''' 初始化 TextContentBlock 的新实例
    ''' </summary>
    Public Sub New()
        Type = "text"
    End Sub

    ''' <summary>
    ''' 获取或设置文本内容
    ''' </summary>
    <JsonProperty("text")>
    Public Property Text As String
End Class

''' <summary>
''' 图片内容块
''' </summary>
Public Class ImageContentBlock
    Inherits ContentBlock

    ''' <summary>
    ''' 初始化 ImageContentBlock 的新实例
    ''' </summary>
    Public Sub New()
        Type = "image"
    End Sub

    ''' <summary>
    ''' 获取或设置 base64 编码的图片数据
    ''' </summary>
    <JsonProperty("data")>
    Public Property Data As String

    ''' <summary>
    ''' 获取或设置 MIME 类型
    ''' </summary>
    <JsonProperty("mimeType")>
    Public Property MimeType As String
End Class

''' <summary>
''' 音频内容块
''' </summary>
Public Class AudioContentBlock
    Inherits ContentBlock

    ''' <summary>
    ''' 初始化 AudioContentBlock 的新实例
    ''' </summary>
    Public Sub New()
        Type = "audio"
    End Sub

    ''' <summary>
    ''' 获取或设置 base64 编码的音频数据
    ''' </summary>
    <JsonProperty("data")>
    Public Property Data As String

    ''' <summary>
    ''' 获取或设置 MIME 类型
    ''' </summary>
    <JsonProperty("mimeType")>
    Public Property MimeType As String
End Class

''' <summary>
''' 嵌入资源内容块
''' </summary>
Public Class EmbeddedResourceBlock
    Inherits ContentBlock

    ''' <summary>
    ''' 初始化 EmbeddedResourceBlock 的新实例
    ''' </summary>
    Public Sub New()
        Type = "resource"
    End Sub

    ''' <summary>
    ''' 获取或设置资源内容
    ''' </summary>
    <JsonProperty("resource")>
    Public Property Resource As Object
End Class

''' <summary>
''' 资源链接内容块
''' </summary>
Public Class ResourceLinkBlock
    Inherits ContentBlock

    ''' <summary>
    ''' 初始化 ResourceLinkBlock 的新实例
    ''' </summary>
    Public Sub New()
        Type = "resource_link"
    End Sub

    ''' <summary>
    ''' 获取或设置资源 URI
    ''' </summary>
    <JsonProperty("uri")>
    Public Property Uri As String

    ''' <summary>
    ''' 获取或设置资源名称
    ''' </summary>
    <JsonProperty("name")>
    Public Property Name As String

    ''' <summary>
    ''' 获取或设置资源描述
    ''' </summary>
    <JsonProperty("description")>
    Public Property Description As String

    ''' <summary>
    ''' 获取或设置 MIME 类型
    ''' </summary>
    <JsonProperty("mimeType")>
    Public Property MimeType As String

    ''' <summary>
    ''' 获取或设置资源大小（字节）
    ''' </summary>
    <JsonProperty("size")>
    Public Property Size As Long?
End Class