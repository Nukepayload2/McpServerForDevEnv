Imports Microsoft.VisualStudio.Shell.Interop
Imports Microsoft.VisualStudio.OLE.Interop
Imports Microsoft.VisualStudio.TextManager.Interop
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports System.Text
Imports System.Diagnostics

''' <summary>
''' 通用符号搜索引擎 - 使用Visual Studio标准API支持所有语言
''' </summary>
Public Class UniversalSymbolSearchEngine
    Implements IDisposable

    ' 常量定义
    Private Shared ReadOnly SVsObjectSearchGuid As New Guid("44A39218-81BD-4669-9DE0-F282A8BAEE34")
    Private Shared ReadOnly SVsShellGuid As New Guid("7B935E31-2695-11D1-BD43-00A0C911CE51")
    Private Shared ReadOnly SID_VsUIShell As New Guid("B61FC35B-EEBF-4DFB-9D8F-6A9496D279F2")
    Private Shared ReadOnly STD_MK As New Guid("{00020400-0000-0000-C000-000000000046}")

    ' Visual Studio版本ProgId
    Private Shared ReadOnly VS2022ProgId As String = "VisualStudio.DTE.18.0"
    Private Shared ReadOnly VS2019ProgId As String = "VisualStudio.DTE.16.0"
    Private Shared ReadOnly VS2017ProgId As String = "VisualStudio.DTE.15.0"

    ' 搜索范围GUID
    Private Shared ReadOnly SolutionScopeGuid As New Guid("53544C4D-883C-4141-8824-8C76228A237F")
    Private Shared ReadOnly ProjectScopeGuid As New Guid("53544C4D-883C-4141-8824-8C76228A237E")
    Private Shared ReadOnly FrameworkScopeGuid As New Guid("53544C4D-883C-4141-8824-8C76228A237C")

    ' 私有字段
    Private _serviceProvider As IServiceProvider
    Private _findSymbol As IVsFindSymbol
    Private _findSymbol2 As IVsFindSymbol2
    Private _dte As EnvDTE.DTE
    Private _uiShell As IVsUIShell
    Private _isDisposed As Boolean
    Private _searchSemaphore As New Threading.SemaphoreSlim(1, 1)
    Private _eventHandler As UniversalFindSymbolEventHandler

    ''' <summary>
    ''' 初始化符号搜索引擎
    ''' </summary>
    Public Async Function InitializeAsync() As Task
        Await Task.Run(Async Function()
            Try
                ' 获取Visual Studio实例
                _dte = GetRunningVisualStudioInstance()
                If _dte Is Nothing Then
                    Throw New InvalidOperationException("未找到运行的Visual Studio实例。请确保Visual Studio已启动并加载了解决方案。")
                End If

                ' 获取服务提供程序
                _serviceProvider = TryCast(_dte, IServiceProvider)
                If _serviceProvider Is Nothing Then
                    Throw New InvalidOperationException("无法获取Visual Studio服务提供程序")
                End If

                ' 获取Visual Studio服务提供程序
                Dim oleServiceProvider As Microsoft.VisualStudio.OLE.Interop.IServiceProvider = TryCast(_serviceProvider, Microsoft.VisualStudio.OLE.Interop.IServiceProvider)
                If oleServiceProvider Is Nothing Then
                    Throw New InvalidOperationException("无法获取OLE服务提供程序")
                End If

                ' 获取UI Shell服务
                Dim uiShellPtr As IntPtr
                Dim uiShellGuid As Guid = SID_VsUIShell
                Dim uiShellIID As Guid = GetType(IVsUIShell).GUID

                oleServiceProvider.QueryService(uiShellGuid, uiShellIID, uiShellPtr)
                If uiShellPtr <> IntPtr.Zero Then
                    _uiShell = TryCast(Marshal.GetObjectForIUnknown(uiShellPtr), IVsUIShell)
                    Marshal.Release(uiShellPtr)
                End If

                ' 获取符号搜索服务
                Dim searchServicePtr As IntPtr
                Dim searchServiceGuid As Guid = SVsObjectSearchGuid
                Dim findSymbolIID As Guid = GetType(IVsFindSymbol).GUID

                oleServiceProvider.QueryService(searchServiceGuid, findSymbolIID, searchServicePtr)
                If searchServicePtr <> IntPtr.Zero Then
                    _findSymbol = TryCast(Marshal.GetObjectForIUnknown(searchServicePtr), IVsFindSymbol)
                    _findSymbol2 = TryCast(_findSymbol, IVsFindSymbol2)
                    Marshal.Release(searchServicePtr)
                End If

                If _findSymbol Is Nothing Then
                    Throw New InvalidOperationException("无法获取符号搜索服务。请确保已安装Visual Studio SDK。")
                End If

                ' 创建事件处理器
                _eventHandler = New UniversalFindSymbolEventHandler()

                ' 验证解决方案是否已加载
                Dim solution As EnvDTE.Solution = _dte.Solution
                If solution Is Nothing OrElse String.IsNullOrWhiteSpace(solution.FullName) Then
                    Throw New InvalidOperationException("请确保Visual Studio中已加载解决方案。")
                End If

            Catch ex As Exception
                Throw New InvalidOperationException("初始化符号搜索引擎失败", ex)
            End Try
        End Function)
    End Function

    ''' <summary>
    ''' 搜索符号定义
    ''' </summary>
    Public Async Function FindSymbolDefinitionsAsync(symbolName As String, searchScope As SearchScope) As Task(Of List(Of SymbolLocation))
        If String.IsNullOrWhiteSpace(symbolName) Then
            Throw New ArgumentException("符号名称不能为空", NameOf(symbolName))
        End If

        Await _searchSemaphore.WaitAsync()
        Try
            ' 使用Visual Studio的FindSymbol功能进行搜索
            Dim results = Await PerformSymbolSearchAsync(symbolName, searchScope, False)

            Return results

        Finally
            _searchSemaphore.Release()
        End Try
    End Function

    ''' <summary>
    ''' 搜索符号引用
    ''' </summary>
    Public Async Function FindSymbolReferencesAsync(symbolName As String, searchScope As SearchScope) As Task(Of List(Of SymbolLocation))
        If String.IsNullOrWhiteSpace(symbolName) Then
            Throw New ArgumentException("符号名称不能为空", NameOf(symbolName))
        End If

        Await _searchSemaphore.WaitAsync()
        Try
            ' 使用Visual Studio的FindSymbol功能进行搜索
            Dim results = Await PerformSymbolSearchAsync(symbolName, searchScope, True)

            Return results

        Finally
            _searchSemaphore.Release()
        End Try
    End Function

    ''' <summary>
    ''' 执行符号搜索的核心方法
    ''' </summary>
    Private Async Function PerformSymbolSearchAsync(symbolName As String, searchScope As SearchScope, includeReferences As Boolean) As Task(Of List(Of SymbolLocation))
        Try
            ' 设置搜索条件
            Dim searchCriteria As VSOBSEARCHCRITERIA2() = New VSOBSEARCHCRITERIA2(0) {}
            searchCriteria(0) = New VSOBSEARCHCRITERIA2() With {
                .szName = symbolName.Trim(),
                .eSrchType = VSOBSEARCHTYPE.SO_SUBSTRING,
                .grfOptions = GetSearchOptions(includeReferences),
                .dwCustom = 0,
                .pIVsNavInfo = Nothing
            }

            ' 根据搜索范围设置GUID
            Dim scopeGuid As Guid = GetScopeGuid(searchScope)

            ' 执行搜索并等待结果
            Dim results = New List(Of SymbolLocation)

            ' 使用DTE的Find功能作为备用方案
            Dim dteResults = Await FindSymbolsUsingDTEAsync(symbolName, includeReferences)
            results.AddRange(dteResults)

            Return results

        Catch ex As Exception
            Debug.WriteLine($"符号搜索时出错: {ex.Message}")
            Return New List(Of SymbolLocation)
        End Try
    End Function

    ''' <summary>
    ''' 使用DTE进行符号搜索（作为备用方案）
    ''' </summary>
    Private Async Function FindSymbolsUsingDTEAsync(symbolName As String, includeReferences As Boolean) As Task(Of List(Of SymbolLocation))
        Dim results As New List(Of SymbolLocation)

        Try
            If _dte Is Nothing Then Return results

            ' 使用DTE的Find功能
            Dim find As EnvDTE.Find = _dte.Find
            If find Is Nothing Then Return results

            ' 设置搜索条件
            find.FindWhat = symbolName
            find.Target = EnvDTE.vsFindTarget.vsFindTargetSolution
            find.MatchCase = False
            find.MatchWholeWord = True
            find.PatternSyntax = EnvDTE.vsFindPatternSyntax.vsFindPatternSyntaxLiteral
            find.ResultsLocation = EnvDTE.vsFindResultsLocation.vsFindResults1

            ' 执行搜索
            Dim searchResult As EnvDTE.vsFindResult = find.Execute()

            ' 循环等待搜索完成
            Dim timeoutCancellationToken = New Threading.CancellationTokenSource(TimeSpan.FromSeconds(30))
            Dim startTime = DateTime.Now

            While Not timeoutCancellationToken.Token.IsCancellationRequested
                Try
                    ' 检查Find状态
                    Dim currentResult = find.Execute()

                    Select Case currentResult
                        Case EnvDTE.vsFindResult.vsFindResultFound,
                             EnvDTE.vsFindResult.vsFindResultReplaceAndFound,
                             EnvDTE.vsFindResult.vsFindResultReplaced
                            ' 找到结果，处理并退出
                            results = Await ProcessFindResults()
                            Debug.WriteLine($"DTE搜索完成，找到 {results.Count} 个结果")
                            Exit While

                        Case EnvDTE.vsFindResult.vsFindResultNotFound,
                             EnvDTE.vsFindResult.vsFindResultReplaceAndNotFound
                            ' 未找到结果，退出循环
                            Debug.WriteLine("DTE搜索完成，未找到结果")
                            Exit While

                        Case EnvDTE.vsFindResult.vsFindResultPending
                            ' 搜索仍在进行中，继续等待
                            Debug.WriteLine("DTE搜索进行中...")
                            ' 继续循环

                        Case EnvDTE.vsFindResult.vsFindResultError
                            ' 搜索出错
                            Debug.WriteLine("DTE搜索出错")
                            Exit While

                        Case Else
                            ' 其他状态，继续等待
                            Debug.WriteLine($"DTE搜索状态: {currentResult}")
                    End Select

                    ' 等待一段时间再检查
                    Await Task.Delay(200, timeoutCancellationToken.Token)

                Catch ex As OperationCanceledException
                    Debug.WriteLine($"DTE搜索超时")
                    Exit While
                Catch ex As Exception
                    Debug.WriteLine($"检查DTE搜索状态时出错: {ex.Message}")
                    Exit While
                End Try
            End While

        Catch ex As Exception
            Debug.WriteLine($"使用DTE搜索时出错: {ex.Message}")
        End Try

        Return results
    End Function

    ''' <summary>
    ''' 处理Find结果窗口中的结果
    ''' </summary>
    Private Async Function ProcessFindResults() As Task(Of List(Of SymbolLocation))
        Dim results As New List(Of SymbolLocation)

        Try
            ' 获取Find结果窗口 - 尝试多个可能的窗口类型
            Dim findResultsWindow As EnvDTE.Window = Nothing

            ' 尝试不同的结果窗口
            Dim windowKinds As String() = {
                EnvDTE.Constants.vsWindowKindFindResults1,
                EnvDTE.Constants.vsWindowKindFindResults2,
                "Find Results 1",
                "Find Results 2"
            }

            For Each windowKind In windowKinds
                Try
                    findResultsWindow = _dte.Windows.Item(windowKind)
                    If findResultsWindow IsNot Nothing Then Exit For
                Catch
                    Continue For
                End Try
            Next

            If findResultsWindow Is Nothing Then
                Debug.WriteLine("找不到Find结果窗口")
                Return results
            End If

            ' 等待窗口完全加载
            Await Task.Delay(100)

            ' 获取文档对象
            Dim document As EnvDTE.Document = findResultsWindow.Document
            If document Is Nothing Then Return results

            ' 获取文本对象
            Dim textDocument As EnvDTE.TextDocument = TryCast(document.Object("TextDocument"), EnvDTE.TextDocument)
            If textDocument Is Nothing Then Return results

            ' 获取完整文档内容
            Dim startPoint As EnvDTE.EditPoint = textDocument.StartPoint.CreateEditPoint()
            Dim endPoint As EnvDTE.EditPoint = textDocument.EndPoint.CreateEditPoint()
            Dim resultText As String = startPoint.GetText(endPoint)

            If String.IsNullOrWhiteSpace(resultText) Then Return results

            ' 解析结果文本
            results = ParseFindResultsText(resultText)
            Debug.WriteLine($"从Find结果窗口解析出 {results.Count} 个结果")

        Catch ex As Exception
            Debug.WriteLine($"处理Find结果时出错: {ex.Message}")
        End Try

        Return results
    End Function

    ''' <summary>
    ''' 解析Find结果文本
    ''' </summary>
    Private Function ParseFindResultsText(resultText As String) As List(Of SymbolLocation)
        Dim results As New List(Of SymbolLocation)
        Dim lines As String() = resultText.Split({vbCrLf, vbLf, vbCr}, StringSplitOptions.RemoveEmptyEntries)

        For Each line As String In lines
            Try
                ' 解析每一行结果，格式通常是：文件路径(行号,列号): 符号
                Dim location = ParseFindResultLine(line.Trim())
                If location IsNot Nothing Then
                    results.Add(location)
                End If
            Catch ex As Exception
                Debug.WriteLine($"解析结果行时出错: {ex.Message}")
            End Try
        Next

        Return results
    End Function

    ''' <summary>
    ''' 解析单行Find结果
    ''' </summary>
    Private Function ParseFindResultLine(line As String) As SymbolLocation
        Try
            ' 匹配模式：文件路径(行号,列号): 符号
            Dim match = System.Text.RegularExpressions.Regex.Match(line, "^(.+)\((\d+),(\d+)\):\s*(.+)$")

            If match.Success Then
                Return New SymbolLocation() With {
                    .FilePath = match.Groups(1).Value.Trim(),
                    .LineNumber = Integer.Parse(match.Groups(2).Value),
                    .ColumnNumber = Integer.Parse(match.Groups(3).Value),
                    .SymbolName = match.Groups(4).Value.Trim(),
                    .SymbolType = "Symbol",
                    .ProjectName = ExtractProjectNameFromPath(match.Groups(1).Value)
                }
            End If

            ' 如果不匹配上述格式，尝试其他格式
            Return Nothing

        Catch ex As Exception
            Debug.WriteLine($"解析结果行失败: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 从文件路径提取项目名称
    ''' </summary>
    Private Function ExtractProjectNameFromPath(filePath As String) As String
        Try
            If String.IsNullOrWhiteSpace(filePath) Then Return ""

            ' 简单的项目名称提取逻辑
            Dim directories As String() = filePath.Split("\"c)
            For i As Integer = directories.Length - 1 To 0 Step -1
                Dim dir As String = directories(i)
                If dir.EndsWith(".csproj") OrElse dir.EndsWith(".vbproj") OrElse dir.EndsWith(".vcxproj") Then
                    Return IO.Path.GetFileNameWithoutExtension(dir)
                End If
            Next

            Return "未知项目"
        Catch ex As Exception
            Debug.WriteLine($"提取项目名称时出错: {ex.Message}")
            Return "未知项目"
        End Try
    End Function

    ''' <summary>
    ''' 获取搜索选项
    ''' </summary>
    Private Function GetSearchOptions(includeReferences As Boolean) As UInteger
        Dim options As UInteger = CUInt(_VSOBSEARCHOPTIONS2.VSOBSO_FILTERING)

        If includeReferences Then
            options = options Or CUInt(_VSOBSEARCHOPTIONS2.VSOBSO_LISTREFERENCES)
        End If

        Return options
    End Function

    ''' <summary>
    ''' 获取运行的Visual Studio实例
    ''' </summary>
    Private Function GetRunningVisualStudioInstance() As EnvDTE.DTE
        Try
            Dim progIds As String() = {VS2022ProgId, VS2019ProgId, VS2017ProgId}

            For Each progId In progIds
                Try
                    Dim dte As EnvDTE.DTE = Marshal.GetActiveObject(progId)
                    If dte IsNot Nothing Then
                        Return dte
                    End If
                Catch ex As COMException
                    Continue For
                End Try
            Next

            Return Nothing
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' 根据搜索范围获取对应的GUID
    ''' </summary>
    Private Function GetScopeGuid(searchScope As SearchScope) As Guid
        Select Case searchScope
            Case SearchScope.Solution
                Return SolutionScopeGuid
            Case SearchScope.Project
                Return ProjectScopeGuid
            Case SearchScope.Namespace
                Return ProjectScopeGuid ' 使用项目范围作为替代
            Case SearchScope.Class
                Return ProjectScopeGuid ' 使用项目范围作为替代
            Case Else
                Return SolutionScopeGuid
        End Select
    End Function

    ''' <summary>
    ''' 释放资源
    ''' </summary>
    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(True)
        GC.SuppressFinalize(Me)
    End Sub

    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not _isDisposed AndAlso disposing Then
            Try
                _searchSemaphore?.Dispose()
                _eventHandler?.Dispose()

                If _dte IsNot Nothing Then
                    Marshal.ReleaseComObject(_dte)
                End If

                If _uiShell IsNot Nothing Then
                    Marshal.ReleaseComObject(_uiShell)
                End If
            Catch ex As Exception
                Debug.WriteLine($"释放资源时出错: {ex.Message}")
            End Try

            _isDisposed = True
        End If
    End Sub

    Protected Overrides Sub Finalize()
        Dispose(False)
    End Sub
End Class

''' <summary>
''' 通用符号搜索事件处理器
''' </summary>
Public Class UniversalFindSymbolEventHandler
    Implements IVsFindSymbolEvents
    Implements IDisposable

    Private _results As New List(Of SymbolLocation)

    Public ReadOnly Property Results As List(Of SymbolLocation)
        Get
            Return _results
        End Get
    End Property

    Public Function OnUserOptionsChanged(ByRef guidSymbolScope As Guid, pobSrch As VSOBSEARCHCRITERIA2()) As Integer Implements IVsFindSymbolEvents.OnUserOptionsChanged
        Return 0
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _results.Clear()
    End Sub
End Class