Imports System.Threading.Tasks
Imports Microsoft.VisualStudio.Interop
Imports EnvDTE80
Imports System.Windows.Threading

Public Class VisualStudioTools
    Private ReadOnly _dte2 As DTE2
    Private ReadOnly _dispatcher As Dispatcher
    Private ReadOnly _mainWindow As MainWindow

    Public Sub New(dte2 As DTE2, dispatcher As Dispatcher, mainWindow As MainWindow)
        _dte2 = dte2
        _dispatcher = dispatcher
        _mainWindow = mainWindow
    End Sub

    Public Async Function BuildSolutionAsync(configuration As String) As Task(Of BuildResult)
        Return Await Task.Run(Function()
                                Return BuildSolutionInternal(configuration)
                            End Function)
    End Function

    Private Function BuildSolutionInternal(configuration As String) As BuildResult
        Dim startTime = DateTime.Now
        Dim result As New BuildResult With {
            .Success = True,
            .Errors = New List(Of CompilationError)(),
            .Warnings = New List(Of CompilationError)(),
            .Configuration = configuration
        }

        Try
            ' 在UI线程上执行DTE操作
            UtilityModule.SafeInvoke(_dispatcher, Sub()
                                                      If _dte2.Solution Is Nothing Then
                                                          Throw New Exception("没有打开的解决方案")
                                                      End If

                                                      ' 设置构建配置
                                                      Dim solutionBuild As EnvDTE.SolutionBuild = _dte2.Solution.SolutionBuild
                                                      solutionBuild.SolutionConfigurations.Item(configuration).Activate()

                                                      ' 清除之前的错误 - 由于没有ClearAll方法，我们跳过这一步

                                                      ' 执行构建
                                                      solutionBuild.Build(True)
                                                  End Sub)

            ' 收集错误和警告（需要在UI线程上执行）
            Dim allErrors = CollectErrorsFromToolWindow()
            result.Errors = allErrors.Errors
            result.Warnings = allErrors.Warnings
            result.Success = result.Errors.Count = 0
            result.BuildTime = DateTime.Now - startTime
            result.Message = If(result.Success,
                                "构建成功完成",
                                $"构建失败，发现 {result.Errors.Count} 个错误和 {result.Warnings.Count} 个警告")

        Catch ex As Exception
            result.Success = False
            result.Message = $"构建过程中发生错误: {ex.Message}"
            result.BuildTime = DateTime.Now - startTime
        End Try

        Return result
    End Function

    Public Async Function BuildProjectAsync(projectName As String, configuration As String) As Task(Of BuildResult)
        Return Await Task.Run(Function()
                                Return BuildProjectInternal(projectName, configuration)
                            End Function)
    End Function

    Private Function BuildProjectInternal(projectName As String, configuration As String) As BuildResult
        Dim startTime = DateTime.Now
        Dim result As New BuildResult With {
            .Success = True,
            .Errors = New List(Of CompilationError)(),
            .Warnings = New List(Of CompilationError)(),
            .Configuration = configuration
        }

        Try
            Dim targetProject As EnvDTE.Project = Nothing

            ' 在UI线程上查找项目
            UtilityModule.SafeInvoke(_dispatcher, Sub()
                                                      If _dte2.Solution Is Nothing Then
                                                          Throw New Exception("没有打开的解决方案")
                                                      End If

                                                      For Each project As EnvDTE.Project In _dte2.Solution.Projects
                                                          If project.Name = projectName Then
                                                              targetProject = project
                                                              Exit For
                                                          End If
                                                      Next

                                                      If targetProject Is Nothing Then
                                                          Throw New Exception($"找不到项目: {projectName}")
                                                      End If

                                                      ' 设置构建配置
                                                      Dim solutionBuild As EnvDTE.SolutionBuild = _dte2.Solution.SolutionBuild
                                                      solutionBuild.SolutionConfigurations.Item(configuration).Activate()

                                                      ' 清除之前的错误 - 由于没有ClearAll方法，我们跳过这一步

                                                      ' 构建特定项目
                                                      solutionBuild.BuildProject(configuration, targetProject.UniqueName, True)
                                                  End Sub)

            ' 收集错误和警告
            Dim allErrors = CollectErrorsFromToolWindow()
            result.Errors = allErrors.Errors
            result.Warnings = allErrors.Warnings
            result.Success = result.Errors.Count = 0
            result.BuildTime = DateTime.Now - startTime
            result.Message = If(result.Success,
                                $"项目 {projectName} 构建成功",
                                $"项目 {projectName} 构建失败，发现 {result.Errors.Count} 个错误和 {result.Warnings.Count} 个警告")

        Catch ex As Exception
            result.Success = False
            result.Message = $"构建项目 {projectName} 时发生错误: {ex.Message}"
            result.BuildTime = DateTime.Now - startTime
        End Try

        Return result
    End Function

    Public Function GetErrorList(Optional severityFilter As String = "All") As BuildResult
        Dim result As New BuildResult With {
            .Errors = New List(Of CompilationError)(),
            .Warnings = New List(Of CompilationError)()
        }

        Try
            result = CollectErrorsFromToolWindow(severityFilter)
        Catch ex As Exception
            _mainWindow?.LogMcpRequest("收集错误", "警告", ex.Message)
        End Try

        Return result
    End Function

    Private Function CollectErrorsFromToolWindow(Optional severityFilter As String = "All") As BuildResult
        Dim result As New BuildResult With {
            .Errors = New List(Of CompilationError)(),
            .Warnings = New List(Of CompilationError)()
        }

        ' 需要在UI线程上执行
        UtilityModule.SafeInvoke(_dispatcher, Sub()
                                                      Try
                                                          ' 获取错误列表工具窗口
                                                          Dim errorList As Object = _dte2.ToolWindows.ErrorList

                                                          If errorList Is Nothing Then
                                                              Return
                                                          End If

                                                          ' 获取错误项集合
                                                          Dim errorItems As Object = errorList.ErrorItems

                                                          If errorItems Is Nothing Then
                                                              Return
                                                          End If

                                                          For i As Integer = 1 To CInt(errorItems.GetType().GetProperty("Count").GetValue(errorItems, Nothing))
                                                              Try
                                                                  Dim errorItem As Object = errorItems.GetType().GetMethod("Item").Invoke(errorItems, New Object() {i})

                                                                  If errorItem IsNot Nothing Then
                                                                      Dim description As String = If(errorItem.GetType().GetProperty("Description").GetValue(errorItem, Nothing), "").ToString()
                                                                      Dim fileName As String = If(errorItem.GetType().GetProperty("FileName").GetValue(errorItem, Nothing), "").ToString()
                                                                      Dim line As Integer = CInt(errorItem.GetType().GetProperty("Line").GetValue(errorItem, Nothing))
                                                                      Dim column As Integer = CInt(errorItem.GetType().GetProperty("Column").GetValue(errorItem, Nothing))
                                                                      Dim errorLevel As Object = errorItem.GetType().GetProperty("ErrorLevel").GetValue(errorItem, Nothing)
                                                                      Dim projectName As String = If(errorItem.GetType().GetProperty("Project").GetValue(errorItem, Nothing), "").ToString()

                                                                      ' 转换错误级别为字符串
                                                                      Dim severity As String
                                                                      ' 使用数值比较而不是枚举
                                                                      Dim errorLevelValue As Integer = CInt(errorLevel)
                                                                      Select Case errorLevelValue
                                                                          Case 1 ' vsBuildErrorLevelHigh
                                                                              severity = "Error"
                                                                          Case 2 ' vsBuildErrorLevelMedium
                                                                              severity = "Warning"
                                                                          Case 3 ' vsBuildErrorLevelLow
                                                                              severity = "Message"
                                                                          Case Else
                                                                              severity = "Unknown"
                                                                      End Select

                                                                      ' 应用过滤器
                                                                      If severityFilter <> "All" AndAlso severity <> severityFilter Then
                                                                          Continue For
                                                                      End If

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

                                                                      Select Case severity
                                                                          Case "Error"
                                                                              result.Errors.Add(compilationError)
                                                                          Case "Warning"
                                                                              result.Warnings.Add(compilationError)
                                                                      End Select
                                                                  End If
                                                              Catch ex As Exception
                                                                  ' 忽略单个错误项的处理错误
                                                              End Try
                                                          Next

                                                      Catch ex As Exception
                                                          ' 记录错误但不抛出异常
                                                          _mainWindow?.LogMcpRequest("收集错误", "警告", ex.Message)
                                                      End Try
                                                  End Sub)

        Return result
    End Function

    Private Function ExtractErrorCode(description As String) As String
        If String.IsNullOrWhiteSpace(description) Then
            Return "UNKNOWN"
        End If

        ' 尝试匹配常见的错误代码格式 (如 BC30002, CS0246 等)
        Dim regex As New System.Text.RegularExpressions.Regex("([A-Z]{2,4}\d{4,6})")
        Dim match As System.Text.RegularExpressions.Match = regex.Match(description)

        If match.Success Then
            Return match.Groups(1).Value
        End If

        Return "GENERAL"
    End Function

    Public Function GetSolutionInformation() As Object
        Dim info = New Dictionary(Of String, Object)

        UtilityModule.SafeInvoke(_dispatcher, Sub()
                                                      If _dte2.Solution IsNot Nothing Then
                                                          info("fullName") = _dte2.Solution.FullName
                                                          info("name") = _dte2.Solution.Name
                                                          info("count") = _dte2.Solution.Count
                                                      End If

                                                      Dim projects = New List(Of Object)
                                                      If _dte2.Solution IsNot Nothing Then
                                                          For i = 1 To _dte2.Solution.Count
                                                              Dim project = _dte2.Solution.Item(i)
                                                              projects.Add(New With {
                                                                  .name = project.Name,
                                                                  .fullName = project.FullName,
                                                                  .uniqueName = project.UniqueName,
                                                                  .kind = project.Kind
                                                              })
                                                          Next
                                                      End If

                                                      info("projects") = projects

                                                      ' 添加当前构建配置信息
                                                      If _dte2.Solution IsNot Nothing AndAlso _dte2.Solution.SolutionBuild IsNot Nothing Then
                                                          Dim activeConfig = _dte2.Solution.SolutionBuild.ActiveConfiguration
                                                          If activeConfig IsNot Nothing Then
                                                              info("activeConfiguration") = New With {
                                                                  .name = activeConfig.Name,
                                                                  .configurationName = activeConfig.Name,
                                                                  .platformName = "Any CPU"
                                                              }
                                                          End If
                                                      End If
                                                  End Sub)

        Return info
    End Function

    Public Async Function CleanSolutionAsync() As Task(Of Object)
        Return Await Task.Run(Function()
                                  Return CleanSolutionInternal()
                              End Function)
    End Function

    Private Function CleanSolutionInternal() As Object
        Try
            UtilityModule.SafeInvoke(_dispatcher, Sub()
                                                      If _dte2.Solution Is Nothing Then
                                                          Throw New Exception("没有打开的解决方案")
                                                      End If

                                                      Dim solutionBuild As EnvDTE.SolutionBuild = _dte2.Solution.SolutionBuild

                                                      ' 执行清理
                                                      solutionBuild.Clean(True)

                                                      ' 清除错误列表 - 由于没有ClearAll方法，我们不需要清除
                                                  End Sub)

            Return New With {
                .success = True,
                .message = "解决方案清理完成"
            }

        Catch ex As Exception
            Return New With {
                .success = False,
                .message = $"清理解决方案失败: {ex.Message}"
            }
        End Try
    End Function
End Class