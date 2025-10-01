Imports System
Imports System.Runtime.InteropServices
Imports System.Threading
Imports Microsoft.VisualBasic
Imports Microsoft.VisualStudio.Shell
Imports Microsoft.VisualStudio.Shell.Interop
Imports Microsoft.VisualStudio.OLE.Interop
Imports Microsoft.VisualStudio.ComponentModelHost
Imports System.ComponentModel.Design
Imports System.Text
Imports System.Collections.Generic
Imports System.Linq
Imports Newtonsoft.Json
Imports System.Threading.Tasks
Imports EnvDTE
Imports EnvDTE80


''' <summary>
''' This is the class that implements the package exposed by this assembly.
''' </summary>
''' <remarks>
''' <para>
''' The minimum requirement for a class to be considered a valid package for Visual Studio
''' Is to implement the IVsPackage interface And register itself with the shell.
''' This package uses the helper classes defined inside the Managed Package Framework (MPF)
''' to do it: it derives from the Package Class that provides the implementation Of the
''' IVsPackage interface And uses the registration attributes defined in the framework to
''' register itself And its components with the shell. These attributes tell the pkgdef creation
''' utility what data to put into .pkgdef file.
''' </para>
''' <para>
''' To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
''' </para>
''' </remarks>
<PackageRegistration(UseManagedResourcesOnly:=True, AllowsBackgroundLoading:=True)>
<ProvideMenuResource("Menus.ctmenu", 1)>
<ProvideToolWindow(GetType(ErrorListToolWindow))>
<Guid(VsCompileAndGetErrorListPackage.PackageGuidString)>
Public NotInheritable Class VsCompileAndGetErrorListPackage
    Inherits AsyncPackage
    Implements IVsUpdateSolutionEvents

    ''' <summary>
    ''' Package guid
    ''' </summary>
    Public Const PackageGuidString As String = "6de424a0-5f15-4ffc-8713-2058016b7f36"

#Region " Member Variables "
    Private _solutionBuildManager As IVsSolutionBuildManager2
    Private _updateSolutionEventsCookie As UInteger
    Private _buildInProgress As Boolean
    Private _buildErrors As New List(Of CompilationError)
#End Region

#Region " Package Members "

    ''' <summary>
    ''' Initialization of the package; this method is called right after the package is sited, so this is the place
    ''' where you can put all the initialization code that rely on services provided by VisualStudio.
    ''' </summary>
    ''' <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
    ''' <param name="progress">A provider for progress updates.</param>
    ''' <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
    Protected Overrides Async Function InitializeAsync(cancellationToken As CancellationToken, progress As IProgress(Of ServiceProgressData)) As Task
        ' When initialized asynchronously, the current thread may be a background thread at this point.
        ' Do any initialization that requires the UI thread after switching to the UI thread.
        Await Me.JoinableTaskFactory.SwitchToMainThreadAsync()

        ' Initialize command services
        Dim commandService As OleMenuCommandService = TryCast(GetService(GetType(IMenuCommandService)), OleMenuCommandService)
        If commandService IsNot Nothing Then
            ' Create compile command
            Dim compileCommandId As New CommandID(GuidList.guidVsCompileAndGetErrorListCmdSet, PkgCmdIDList.cmdidCompileProject)
            Dim compileMenuItem As New MenuCommand(AddressOf CompileProjectCallback, compileCommandId)
            commandService.AddCommand(compileMenuItem)

            ' Create show error list command
            Dim showErrorListCommandId As New CommandID(GuidList.guidVsCompileAndGetErrorListCmdSet, PkgCmdIDList.cmdidShowErrorList)
            Dim showErrorListMenuItem As New MenuCommand(AddressOf ShowErrorListCallback, showErrorListCommandId)
            commandService.AddCommand(showErrorListMenuItem)
        End If

        ' Get solution build manager and subscribe to build events
        _solutionBuildManager = TryCast(GetService(GetType(SVsSolutionBuildManager)), IVsSolutionBuildManager2)
        If _solutionBuildManager IsNot Nothing Then
            _solutionBuildManager.AdviseUpdateSolutionEvents(Me, _updateSolutionEventsCookie)
        End If
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            ' Unadvise from build events
            If _solutionBuildManager IsNot Nothing AndAlso _updateSolutionEventsCookie <> 0 Then
                _solutionBuildManager.UnadviseUpdateSolutionEvents(_updateSolutionEventsCookie)
            End If
        End If
        MyBase.Dispose(disposing)
    End Sub

    ''' <summary>
    ''' 获取异步工具窗口工厂
    ''' </summary>
    Public Overrides Function GetAsyncToolWindowFactory(toolWindowType As Guid) As IVsAsyncToolWindowFactory
        Return If(toolWindowType.Equals(Guid.Parse(ErrorListToolWindow.WindowGuidString)), Me, Nothing)
    End Function

    ''' <summary>
    ''' 获取工具窗口标题
    ''' </summary>
    Protected Overrides Function GetToolWindowTitle(toolWindowType As Type, id As Integer) As String
        Return If(toolWindowType = GetType(ErrorListToolWindow), ErrorListToolWindow.Title, MyBase.GetToolWindowTitle(toolWindowType, id))
    End Function

    ''' <summary>
    ''' 在后台线程初始化工具窗口状态
    ''' </summary>
    Protected Overrides Async Function InitializeToolWindowAsync(toolWindowType As Type, id As Integer, cancellationToken As CancellationToken) As Task(Of Object)
        ' 在后台线程执行尽可能多的工作
        ' 此方法返回的对象将传递到 ErrorListToolWindow 的构造函数中
        Dim dte = Await GetServiceAsync(GetType(DTE))
        Dim dte2 = TryCast(dte, DTE2)

        Return New ErrorListToolWindowState With {
            .DTE = dte2
        }
    End Function

#End Region

#Region " Command Callbacks "

    Private Sub CompileProjectCallback(sender As Object, e As EventArgs)
        If Not _buildInProgress Then
            ' Clear previous errors
            _buildErrors.Clear()

            ' Collect real errors from tool window
            CollectErrorsFromToolWindow()

            ' Show error list automatically
            ShowErrorListCallback(Nothing, EventArgs.Empty)
        End If
    End Sub

    Private Sub ShowErrorListCallback(sender As Object, e As EventArgs)
        ' Show the error list tool window
        Dim toolWindow = FindToolWindow(GetType(ErrorListToolWindow), 0, True)
        If toolWindow IsNot Nothing AndAlso toolWindow.Frame IsNot Nothing Then
            Dim windowFrame As IVsWindowFrame = TryCast(toolWindow.Frame, IVsWindowFrame)
            windowFrame?.Show()

            ' Update the tool window with current errors
            Dim errorToolWindow = TryCast(toolWindow, ErrorListToolWindow)
            errorToolWindow?.UpdateErrors(_buildErrors)
        End If
    End Sub

#End Region

#Region " Build Event Handlers "

    Private Function UpdateSolution_Begin(ByRef pfCancelUpdate As Integer) As Integer Implements IVsUpdateSolutionEvents.UpdateSolution_Begin
        _buildInProgress = True
        _buildErrors.Clear()
        Return 0 ' S_OK
    End Function

    Private Function UpdateSolution_Done(fSucceeded As Integer, fModified As Integer, fCancelCommand As Integer) As Integer Implements IVsUpdateSolutionEvents.UpdateSolution_Done
        _buildInProgress = False

        ' Collect errors after build is complete
        CollectErrorsFromToolWindow()

        ' Show error list automatically if there are errors
        If _buildErrors.Count > 0 Then
            ShowErrorListCallback(Nothing, EventArgs.Empty)
        End If

        Return 0 ' S_OK
    End Function

    Private Function UpdateSolution_StartUpdate(ByRef pfCancelUpdate As Integer) As Integer Implements IVsUpdateSolutionEvents.UpdateSolution_StartUpdate
        Return 0 ' S_OK
    End Function

    Private Function UpdateSolution_Cancel() As Integer Implements IVsUpdateSolutionEvents.UpdateSolution_Cancel
        _buildInProgress = False
        Return 0 ' S_OK
    End Function

    Private Function OnActiveProjectCfgChange(pIVsHierarchy As IVsHierarchy) As Integer Implements IVsUpdateSolutionEvents.OnActiveProjectCfgChange
        Return 0 ' S_OK
    End Function

#End Region

#Region " Error Collection "

    ''' <summary>
    ''' 从工具窗口收集错误（使用异步初始化的 DTE 服务）
    ''' </summary>
    Private Sub CollectErrorsFromToolWindow()
        Try
            ' 获取工具窗口实例
            Dim toolWindow = FindToolWindow(GetType(ErrorListToolWindow), 0, False)
            If toolWindow Is Nothing Then
                System.Diagnostics.Debug.WriteLine("工具窗口未初始化")
                Return
            End If

            Dim errorToolWindow = TryCast(toolWindow, ErrorListToolWindow)
            If errorToolWindow Is Nothing Then
                System.Diagnostics.Debug.WriteLine("无法转换为 ErrorListToolWindow")
                Return
            End If

            ' 使用工具窗口的方法收集错误
            Dim errors = errorToolWindow.CollectErrorsFromToolWindow()
            _buildErrors.AddRange(errors)

            System.Diagnostics.Debug.WriteLine($"成功收集 {_buildErrors.Count} 个错误")

        Catch ex As Exception
            System.Diagnostics.Debug.WriteLine($"收集错误时出错: {ex.Message}")
        End Try
    End Sub

#End Region

End Class
