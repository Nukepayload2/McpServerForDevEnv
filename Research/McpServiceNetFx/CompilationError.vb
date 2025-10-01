Public Class CompilationError
    Public Property ErrorCode As String
    Public Property Message As String
    Public Property File As String
    Public Property Line As Integer
    Public Property Column As Integer
    Public Property Project As String
    Public Property Severity As String
End Class

Public Class BuildResult
    Public Property Success As Boolean
    Public Property Errors As List(Of CompilationError)
    Public Property Warnings As List(Of CompilationError)
    Public Property BuildTime As TimeSpan
    Public Property Configuration As String
    Public Property Message As String
End Class