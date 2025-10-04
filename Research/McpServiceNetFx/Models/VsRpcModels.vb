Imports Newtonsoft.Json

Public Class CompilationError

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

' 打开文档响应强类型
Public Class OpenDocumentsResponse
    <JsonProperty("documents")>
    Public Property Documents As DocumentInfo()

    <JsonProperty("totalCount")>
    Public Property TotalCount As Integer
End Class

' 文档信息强类型
Public Class DocumentInfo
    <JsonProperty("path")>
    Public Property Path As String

    <JsonProperty("name")>
    Public Property Name As String

    <JsonProperty("isSaved")>
    Public Property IsSaved As Boolean

    <JsonProperty("language")>
    Public Property Language As String

    <JsonProperty("projectName")>
    Public Property ProjectName As String
End Class
