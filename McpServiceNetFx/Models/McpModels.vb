Imports Newtonsoft.Json

' MCP ���߶������ʽ����
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

' MCP ��׼������Ӧ��
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
''' ��ʾ MCP ���ߵ��óɹ�����Ļ���
''' </summary>
Public MustInherit Class CallToolResultBase
    ''' <summary>
    ''' ��ȡ��������Ӧ�����б�
    ''' </summary>
    <JsonProperty("content")>
    Public Property Content As IList(Of ContentBlock) = New List(Of ContentBlock)()

    ''' <summary>
    ''' ��ȡ�����ÿ�ѡ�Ľṹ�����
    ''' </summary>
    <JsonProperty("structuredContent")>
    Public Property StructuredContent As Object
End Class

''' <summary>
''' ��ʾ MCP ���ߵ��óɹ����
''' </summary>
''' <remarks>
''' ���߳ɹ�ִ�к󷵻صĽ�����������ߵ��������
''' </remarks>
Public Class CallToolSuccessResult
    Inherits CallToolResultBase

    ''' <summary>
    ''' ��ʼ�� CallToolSuccessResult ����ʵ��
    ''' </summary>
    Public Sub New()
        MyBase.New()
    End Sub

    ''' <summary>
    ''' ʹ��ָ�������ݳ�ʼ�� CallToolSuccessResult ����ʵ��
    ''' </summary>
    ''' <param name="content">���߷��ص�����</param>
    Public Sub New(content As IList(Of ContentBlock))
        Me.New()
        Me.Content = content
    End Sub

    ''' <summary>
    ''' ʹ��ָ�����ı����ݳ�ʼ�� CallToolSuccessResult ����ʵ��
    ''' </summary>
    ''' <param name="text">���߷��ص��ı�����</param>
    Public Sub New(text As String)
        Me.New()
        If Not String.IsNullOrEmpty(text) Then
            Content.Add(New TextContentBlock With {.Text = text})
        End If
    End Sub
End Class

''' <summary>
''' ��ʾ MCP ���ߵ��ô�����
''' </summary>
''' <remarks>
''' ����ִ��ʧ��ʱ���صĴ�����������������Ϣ����ϸ��Ϣ
''' </remarks>
Public Class CallToolErrorResult
    Inherits CallToolResultBase

    ''' <summary>
    ''' ��ȡ�����ô���ָʾ����ʼ��Ϊ true
    ''' </summary>
    <JsonProperty("isError")>
    Public Property IsError As Boolean = True

    ''' <summary>
    ''' ��ȡ�����ô�����Ϣ
    ''' </summary>
    <JsonProperty("errorMessage")>
    Public Property ErrorMessage As String

    ''' <summary>
    ''' ��ȡ�����ô������
    ''' </summary>
    <JsonProperty("errorCode")>
    Public Property ErrorCode As String

    ''' <summary>
    ''' ��ȡ�����ô�������
    ''' </summary>
    <JsonProperty("errorDetails")>
    Public Property ErrorDetails As Object

    ''' <summary>
    ''' ��ʼ�� CallToolErrorResult ����ʵ��
    ''' </summary>
    Public Sub New()
        MyBase.New()
    End Sub

    ''' <summary>
    ''' ʹ��ָ���Ĵ�����Ϣ��ʼ�� CallToolErrorResult ����ʵ��
    ''' </summary>
    ''' <param name="errorMessage">������Ϣ</param>
    Public Sub New(errorMessage As String)
        Me.New()
        Me.ErrorMessage = errorMessage
    End Sub

    ''' <summary>
    ''' ʹ��ָ���Ĵ�����Ϣ�ʹ�������ʼ�� CallToolErrorResult ����ʵ��
    ''' </summary>
    ''' <param name="errorMessage">������Ϣ</param>
    ''' <param name="errorCode">�������</param>
    Public Sub New(errorMessage As String, errorCode As String)
        Me.New(errorMessage)
        Me.ErrorCode = errorCode
    End Sub

    ''' <summary>
    ''' ʹ��ָ���Ĵ�����Ϣ���������ʹ��������ʼ�� CallToolErrorResult ����ʵ��
    ''' </summary>
    ''' <param name="errorMessage">������Ϣ</param>
    ''' <param name="errorCode">�������</param>
    ''' <param name="errorDetails">��������</param>
    Public Sub New(errorMessage As String, errorCode As String, errorDetails As Object)
        Me.New(errorMessage, errorCode)
        Me.ErrorDetails = errorDetails
    End Sub
End Class

''' <summary>
''' ���ݿ����
''' </summary>
Public MustInherit Class ContentBlock
    ''' <summary>
    ''' ��ȡ��������������
    ''' </summary>
    <JsonProperty("type")>
    Public Property Type As String = String.Empty

    ''' <summary>
    ''' ��ȡ�����ÿ�ѡ��ע��
    ''' </summary>
    <JsonProperty("annotations")>
    Public Property Annotations As Object
End Class

''' <summary>
''' �ı����ݿ�
''' </summary>
Public Class TextContentBlock
    Inherits ContentBlock

    ''' <summary>
    ''' ��ʼ�� TextContentBlock ����ʵ��
    ''' </summary>
    Public Sub New()
        Type = "text"
    End Sub

    ''' <summary>
    ''' ��ȡ�������ı�����
    ''' </summary>
    <JsonProperty("text")>
    Public Property Text As String
End Class

''' <summary>
''' ͼƬ���ݿ�
''' </summary>
Public Class ImageContentBlock
    Inherits ContentBlock

    ''' <summary>
    ''' ��ʼ�� ImageContentBlock ����ʵ��
    ''' </summary>
    Public Sub New()
        Type = "image"
    End Sub

    ''' <summary>
    ''' ��ȡ������ base64 �����ͼƬ����
    ''' </summary>
    <JsonProperty("data")>
    Public Property Data As String

    ''' <summary>
    ''' ��ȡ������ MIME ����
    ''' </summary>
    <JsonProperty("mimeType")>
    Public Property MimeType As String
End Class

''' <summary>
''' ��Ƶ���ݿ�
''' </summary>
Public Class AudioContentBlock
    Inherits ContentBlock

    ''' <summary>
    ''' ��ʼ�� AudioContentBlock ����ʵ��
    ''' </summary>
    Public Sub New()
        Type = "audio"
    End Sub

    ''' <summary>
    ''' ��ȡ������ base64 �������Ƶ����
    ''' </summary>
    <JsonProperty("data")>
    Public Property Data As String

    ''' <summary>
    ''' ��ȡ������ MIME ����
    ''' </summary>
    <JsonProperty("mimeType")>
    Public Property MimeType As String
End Class

''' <summary>
''' Ƕ����Դ���ݿ�
''' </summary>
Public Class EmbeddedResourceBlock
    Inherits ContentBlock

    ''' <summary>
    ''' ��ʼ�� EmbeddedResourceBlock ����ʵ��
    ''' </summary>
    Public Sub New()
        Type = "resource"
    End Sub

    ''' <summary>
    ''' ��ȡ��������Դ����
    ''' </summary>
    <JsonProperty("resource")>
    Public Property Resource As Object
End Class

''' <summary>
''' ��Դ�������ݿ�
''' </summary>
Public Class ResourceLinkBlock
    Inherits ContentBlock

    ''' <summary>
    ''' ��ʼ�� ResourceLinkBlock ����ʵ��
    ''' </summary>
    Public Sub New()
        Type = "resource_link"
    End Sub

    ''' <summary>
    ''' ��ȡ��������Դ URI
    ''' </summary>
    <JsonProperty("uri")>
    Public Property Uri As String

    ''' <summary>
    ''' ��ȡ��������Դ����
    ''' </summary>
    <JsonProperty("name")>
    Public Property Name As String

    ''' <summary>
    ''' ��ȡ��������Դ����
    ''' </summary>
    <JsonProperty("description")>
    Public Property Description As String

    ''' <summary>
    ''' ��ȡ������ MIME ����
    ''' </summary>
    <JsonProperty("mimeType")>
    Public Property MimeType As String

    ''' <summary>
    ''' ��ȡ��������Դ��С���ֽڣ�
    ''' </summary>
    <JsonProperty("size")>
    Public Property Size As Long?
End Class


' JSON-RPC 2.0 ����ģ�� - ʹ�� Newtonsoft.Json ���л�
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
