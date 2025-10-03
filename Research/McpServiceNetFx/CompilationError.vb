Imports Newtonsoft.Json

Public Class CompilationError
    <JsonProperty("errorCode")>
    Public Property ErrorCode As String

    <JsonProperty("message")>
    Public Property Message As String

    <JsonProperty("file")>
    Public Property File As String

    <JsonProperty("line")>
    Public Property Line As Integer

    <JsonProperty("column")>
    Public Property Column As Integer

    <JsonProperty("project")>
    Public Property Project As String

    <JsonProperty("severity")>
    Public Property Severity As String
End Class

Public Class BuildResult
    <JsonProperty("success")>
    Public Property Success As Boolean

    <JsonProperty("errors", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Errors As List(Of CompilationError)

    <JsonProperty("warnings", DefaultValueHandling:=DefaultValueHandling.Ignore)>
    Public Property Warnings As List(Of CompilationError)

    <JsonProperty("buildTime")>
    Public Property BuildTime As TimeSpan

    <JsonProperty("configuration")>
    Public Property Configuration As String

    <JsonProperty("message")>
    Public Property Message As String
End Class