Imports System

Public NotInheritable Class GuidList
    Public Const guidVsCompileAndGetErrorListPkgString As String = "6de424a0-5f15-4ffc-8713-2058016b7f36"
    Public Const guidVsCompileAndGetErrorListCmdSetString As String = "8a4b5b2e-9f1a-4a3d-8b7c-6d9e9f0a1b2c"

    Public Shared ReadOnly guidVsCompileAndGetErrorListCmdSet As New Guid(guidVsCompileAndGetErrorListCmdSetString)
End Class

Public NotInheritable Class PkgCmdIDList
    Public Const cmdidCompileProject As Integer = &H2001
    Public Const cmdidShowErrorList As Integer = &H2002
End Class

Public Class CompilationError
    Public Property ErrorCode As String
    Public Property Message As String
    Public Property File As String
    Public Property Line As Integer
    Public Property Column As Integer
    Public Property Project As String
    Public Property Severity As String
End Class