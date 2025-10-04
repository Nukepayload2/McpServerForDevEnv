Imports EnvDTE80
Imports System.Windows.Threading

Public Class VisualStudioTools
    Private ReadOnly _dte2 As DTE2
    Private ReadOnly _dispatcher As Dispatcher
    Private ReadOnly _logger As IMcpLogger

    Public Sub New(dte2 As DTE2, dispatcher As Dispatcher, logger As IMcpLogger)
        _dte2 = dte2
        _dispatcher = dispatcher
        _logger = logger
    End Sub

    Public Async Function BuildSolutionAsync(configuration As String) As Task(Of BuildResult)
        Return Await BuildSolutionInternal(configuration)
    End Function

    Private Async Function BuildSolutionInternal(configuration As String) As Task(Of BuildResult)
        Dim startTime = DateTime.Now
        Dim result As New BuildResult With {
            .Success = True,
            .Errors = New List(Of CompilationError)(),
            .Warnings = New List(Of CompilationError)(),
            .Configuration = configuration
        }

        Try
            ' 在UI线程上执行DTE操作
            Await UtilityModule.SafeInvokeAsync(_dispatcher,
            Async Function()
                If _dte2.Solution Is Nothing Then
                    Throw New Exception("没有打开的解决方案")
                End If

                ' 设置构建配置
                Dim solutionBuild As EnvDTE.SolutionBuild = _dte2.Solution.SolutionBuild
                solutionBuild.SolutionConfigurations.Item(configuration).Activate()

                ' 清除之前的错误 - 由于没有ClearAll方法，我们跳过这一步

                ' 执行构建（不等待完成）
                solutionBuild.Build(False)

                ' 轮询构建状态
                Do While solutionBuild.BuildState = EnvDTE.vsBuildState.vsBuildStateInProgress
                    Await Task.Delay(100)
                Loop
            End Function)

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
        Return Await BuildProjectInternal(projectName, configuration)
    End Function

    Private Async Function BuildProjectInternal(projectName As String, configuration As String) As Task(Of BuildResult)
        Dim startTime = DateTime.Now
        Dim result As New BuildResult With {
            .Success = True,
            .Errors = New List(Of CompilationError)(),
            .Warnings = New List(Of CompilationError)(),
            .Configuration = configuration
        }

        Try
            Dim targetProject As EnvDTE.Project = Nothing

            ' 在UI线程上查找项目并启动构建
            Await UtilityModule.SafeInvokeAsync(_dispatcher,
            Async Function()
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

                ' 构建特定项目（不等待完成）
                solutionBuild.BuildProject(configuration, targetProject.UniqueName, False)

                ' 轮询构建状态
                Do While solutionBuild.BuildState = EnvDTE.vsBuildState.vsBuildStateInProgress
                    Await Task.Delay(100)
                Loop
            End Function)

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
            _logger?.LogMcpRequest("收集错误", "警告", ex.Message)
        End Try

        Return result
    End Function

    Private Function CollectErrorsFromToolWindow(Optional severityFilter As String = "All") As BuildResult
        Dim result As New BuildResult With {
            .Errors = New List(Of CompilationError)(),
            .Warnings = New List(Of CompilationError)()
        }

        ' 需要在UI线程上执行
        UtilityModule.SafeInvoke(_dispatcher,
        Sub()
            Try
                ' 获取错误列表工具窗口
                Dim errorList = _dte2.ToolWindows.ErrorList

                If errorList Is Nothing Then
                    Return
                End If

                ' 获取错误项集合
                Dim errorItems = errorList.ErrorItems

                If errorItems Is Nothing Then
                    Return
                End If

                For i = 1 To errorItems.Count
                    Try
                        Dim errorItem = errorItems.Item(i)

                        If errorItem IsNot Nothing Then
                            Dim description As String = If(errorItem.Description, "")
                            Dim fileName As String = If(errorItem.FileName, "")
                            Dim line As Integer = CInt(errorItem.Line)
                            Dim column As Integer = CInt(errorItem.Column)
                            Dim errorLevel = errorItem.ErrorLevel
                            Dim projectName As String = If(errorItem.Project, "")

                            ' 转换错误级别为字符串
                            Dim severity As String
                            Select Case errorLevel
                                Case vsBuildErrorLevel.vsBuildErrorLevelLow
                                    severity = "Message"
                                Case vsBuildErrorLevel.vsBuildErrorLevelMedium
                                    severity = "Warning"
                                Case vsBuildErrorLevel.vsBuildErrorLevelHigh
                                    severity = "Error"
                                Case Else
                                    severity = "Unknown"
                            End Select

                            ' 应用过滤器
                            If severityFilter <> "All" AndAlso severity <> severityFilter Then
                                Continue For
                            End If

                            ' 创建 CompilationError 对象
                            Dim compilationError As New CompilationError With {
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
                _logger?.LogMcpRequest("收集错误", "警告", ex.Message)
            End Try
        End Sub)

        Return result
    End Function

    Public Function GetSolutionInformation() As SolutionInfoResponse
        Dim response As New SolutionInfoResponse()

        UtilityModule.SafeInvoke(_dispatcher,
        Sub()
            If _dte2.Solution IsNot Nothing Then
                response.FullName = _dte2.Solution.FullName
                response.Count = _dte2.Solution.Count
            End If

            Dim projectsList = New List(Of ProjectInfo)
            If _dte2.Solution IsNot Nothing Then
                For i = 1 To _dte2.Solution.Count
                    Dim project = _dte2.Solution.Item(i)
                    projectsList.Add(New ProjectInfo With {
                        .Name = project.Name,
                        .FullName = project.FullName,
                        .UniqueName = project.UniqueName,
                        .Kind = project.Kind
                    })
                Next
            End If

            response.Projects = projectsList.ToArray()

            ' 添加当前构建配置信息
            If _dte2.Solution IsNot Nothing AndAlso _dte2.Solution.SolutionBuild IsNot Nothing Then
                Dim activeConfig = _dte2.Solution.SolutionBuild.ActiveConfiguration
                If activeConfig IsNot Nothing Then
                    response.ActiveConfiguration = New ConfigurationInfo With {
                        .Name = activeConfig.Name,
                        .ConfigurationName = activeConfig.Name,
                        .PlatformName = "Any CPU"
                    }
                End If
            End If
        End Sub)

        Return response
    End Function

    ''' <summary>
    ''' 获取当前活动文档的信息
    ''' </summary>
    ''' <returns>包含活动文档信息的 ActiveDocumentResponse 对象</returns>
    Public Function GetActiveDocument() As ActiveDocumentResponse
        Dim documentPath As String = Nothing

        UtilityModule.SafeInvoke(_dispatcher,
        Sub()
            Try
                ' 检查是否有活动文档
                If _dte2.ActiveDocument IsNot Nothing Then
                    documentPath = _dte2.ActiveDocument.FullName
                End If
            Catch ex As Exception
                _logger?.LogMcpRequest("获取活动文档", "警告", ex.Message)
            End Try
        End Sub)

        ' 创建响应对象
        Dim response As New ActiveDocumentResponse With {
            .HasActiveDocument = Not String.IsNullOrEmpty(documentPath)
        }

        If response.HasActiveDocument Then
            response.Path = documentPath
        End If

        Return response
    End Function

    ''' <summary>
    ''' 获取所有打开文档的信息
    ''' </summary>
    ''' <returns>包含所有打开文档信息的 OpenDocumentsResponse 对象</returns>
    Public Function GetAllOpenDocuments() As OpenDocumentsResponse
        Dim documentsList As New List(Of DocumentInfo)

        UtilityModule.SafeInvoke(_dispatcher,
        Sub()
            Try
                ' 遍历所有打开的文档
                For Each document As EnvDTE.Document In _dte2.Documents
                    If document IsNot Nothing Then
                        Dim docInfo As New DocumentInfo With {
                            .Path = If(document.FullName, ""),
                            .Name = If(document.Name, ""),
                            .IsSaved = document.Saved,
                            .Language = If(document.Language, ""),
                            .ProjectName = ""
                        }

                        ' 尝试获取文档所属的项目名称
                        Try
                            If document.ProjectItem IsNot Nothing AndAlso document.ProjectItem.ContainingProject IsNot Nothing Then
                                docInfo.ProjectName = document.ProjectItem.ContainingProject.Name
                            End If
                        Catch ex As Exception
                            ' 忽略获取项目名称时的错误
                        End Try

                        documentsList.Add(docInfo)
                    End If
                Next
            Catch ex As Exception
                _logger?.LogMcpRequest("获取所有打开文档", "警告", ex.Message)
            End Try
        End Sub)

        ' 创建响应对象
        Dim response As New OpenDocumentsResponse With {
            .Documents = documentsList.ToArray(),
            .TotalCount = documentsList.Count
        }

        Return response
    End Function

End Class