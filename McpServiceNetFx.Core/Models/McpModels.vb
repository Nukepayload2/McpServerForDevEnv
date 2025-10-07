Imports Newtonsoft.Json

' MCP 工具定义的显式类型
Public Class ToolsListResponse
    <JsonProperty("tools")>
    Public Property Tools As ToolDefinition()
End Class

Public Class ToolDefinition
    <JsonProperty("name")>
    Public Property Name As String

    <JsonProperty("description")>
    Public Property Description As String

    <JsonProperty("inputSchema")>
    Public Property InputSchema As InputSchema
End Class

''' <summary>
''' 运行自定义工具的结果
''' </summary>
Public Class RunCustomToolsResult
    ''' <summary>
    ''' 是否成功
    ''' </summary>
    <JsonProperty("success")>
    Public Property Success As Boolean

    ''' <summary>
    ''' 结果消息
    ''' </summary>
    <JsonProperty("message")>
    Public Property Message As String

    ''' <summary>
    ''' 已处理的文件列表
    ''' </summary>
    <JsonProperty("processedFiles")>
    Public Property ProcessedFiles As String()

    ''' <summary>
    ''' 错误信息（如果有）
    ''' </summary>
    <JsonProperty("errors")>
    Public Property Errors As String
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
