' 自定义 MCP 异常类，用于替换 ModelContextProtocol 库中的异常类型
Public Class McpException
    Inherits Exception

    Private ReadOnly _errorCode As String

    Public Sub New(message As String)
        MyBase.New(message)
        _errorCode = "InternalError"
    End Sub

    Public Sub New(message As String, errorCode As String)
        MyBase.New(message)
        _errorCode = errorCode
    End Sub

    Public Sub New(message As String, innerException As Exception)
        MyBase.New(message, innerException)
        _errorCode = "InternalError"
    End Sub

    Public Sub New(message As String, errorCode As String, innerException As Exception)
        MyBase.New(message, innerException)
        _errorCode = errorCode
    End Sub

    Public ReadOnly Property ErrorCode As String
        Get
            Return _errorCode
        End Get
    End Property
End Class

' MCP 错误代码枚举
Public Module McpErrorCode
    Public Const [InvalidParams] As String = "-32602"
    Public Const [InternalError] As String = "-32603"
    Public Const [MethodNotFound] As String = "-32601"
    Public Const [InvalidRequest] As String = "-32600"
    Public Const [ParseError] As String = "-32700"
End Module