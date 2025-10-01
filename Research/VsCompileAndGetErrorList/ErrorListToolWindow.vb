Imports System
Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports Microsoft.VisualStudio.Shell
Imports System.Windows.Controls
Imports EnvDTE
Imports EnvDTE80

<Guid("e4e2ba26-a455-4c53-adb3-8225fb696f8b")>
Public Class ErrorListToolWindow
    Inherits ToolWindowPane

    Public Const WindowGuidString As String = "e4e2ba26-a455-4c53-adb3-8225fb696f8b"
    Public Const Title As String = "编译错误列表 (JSON)"

    Private _errorListControl As ErrorListControl
    Private _state As ErrorListToolWindowState

    ' state 参数是从 MyPackage.InitializeToolWindowAsync 返回的对象
    Public Sub New(state As ErrorListToolWindowState)
        MyBase.New()
        Caption = If(state?.WindowTitle, Title)
        _state = state
        _errorListControl = New ErrorListControl()
        Content = _errorListControl
    End Sub

    Public Sub UpdateErrors(errors As List(Of CompilationError))
        If _errorListControl IsNot Nothing Then
            _errorListControl.UpdateErrors(errors)
        End If
    End Sub

    ''' <summary>
    ''' 使用传入的 DTE 服务收集错误
    ''' </summary>
    Public Function CollectErrorsFromToolWindow() As List(Of CompilationError)
        Dim errors As New List(Of CompilationError)

        Try
            If _state?.DTE Is Nothing Then
                System.Diagnostics.Debug.WriteLine("DTE2 服务未初始化")
                Return errors
            End If

            ' 获取错误列表
            Dim errorList As ErrorList = _state.DTE.ToolWindows.ErrorList

            If errorList Is Nothing Then
                System.Diagnostics.Debug.WriteLine("无法获取错误列表")
                Return errors
            End If

            ' 获取错误项集合
            Dim errorItems As ErrorItems = errorList.ErrorItems

            If errorItems Is Nothing Then
                System.Diagnostics.Debug.WriteLine("无法获取错误项集合")
                Return errors
            End If

            ' 遍历所有错误
            For i As Integer = 1 To errorItems.Count
                Try
                    ' 获取单个错误项
                    Dim errorItem As ErrorItem = errorItems.Item(i)

                    If errorItem IsNot Nothing Then
                        ' 提取错误信息
                        Dim description As String = If(errorItem.Description, "")
                        Dim fileName As String = If(errorItem.FileName, "")
                        Dim line As Integer = errorItem.Line
                        Dim column As Integer = errorItem.Column
                        Dim errorLevel As vsBuildErrorLevel = errorItem.ErrorLevel
                        Dim projectName As String = If(errorItem.Project, "")

                        ' 转换错误级别为字符串
                        Dim severity As String
                        Select Case errorLevel
                            Case vsBuildErrorLevel.vsBuildErrorLevelHigh
                                severity = "Error"
                            Case vsBuildErrorLevel.vsBuildErrorLevelMedium
                                severity = "Warning"
                            Case vsBuildErrorLevel.vsBuildErrorLevelLow
                                severity = "Message"
                            Case Else
                                severity = "Unknown"
                        End Select

                        ' 创建 CompilationError 对象
                        Dim compilationError As New CompilationError With {
                            .ErrorCode = ExtractErrorCode(description),
                            .Message = description,
                            .File = fileName,
                            .Line = line,
                            .Column = column,
                            .Project = projectName,
                            .Severity = severity
                        }

                        errors.Add(compilationError)
                    End If
                Catch ex As Exception
                    System.Diagnostics.Debug.WriteLine($"处理错误项 {i} 时出错: {ex.Message}")
                End Try
            Next

            System.Diagnostics.Debug.WriteLine($"成功收集 {errors.Count} 个错误")

        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine($"收集错误时出错: {ex.Message}")
        End Try

        Return errors
    End Function

    ''' <summary>
    ''' 从错误描述中提取错误代码
    ''' </summary>
    Private Function ExtractErrorCode(description As String) As String
        If String.IsNullOrWhiteSpace(description) Then
            Return "UNKNOWN"
        End If

        ' 尝试匹配常见的错误代码格式 (如 BC30002, CS0246 等)
        Dim regex As New Text.RegularExpressions.Regex("([A-Z]{2,4}\d{4,6})")
        Dim match As Text.RegularExpressions.Match = regex.Match(description)

        If match.Success Then
            Return match.Groups(1).Value
        End If

        Return "GENERAL"
    End Function
End Class