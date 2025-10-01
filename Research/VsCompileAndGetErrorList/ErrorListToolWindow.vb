Imports System
Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports Microsoft.VisualStudio.Shell
Imports System.Windows.Controls

Public Class ErrorListToolWindow
    Inherits ToolWindowPane

    Private _errorListControl As ErrorListControl

    Public Sub New()
        MyBase.New(Nothing)
        Caption = "编译错误列表 (JSON)"
        _errorListControl = New ErrorListControl()
        Content = _errorListControl
    End Sub

    Public Sub UpdateErrors(errors As List(Of CompilationError))
        If _errorListControl IsNot Nothing Then
            _errorListControl.UpdateErrors(errors)
        End If
    End Sub
End Class