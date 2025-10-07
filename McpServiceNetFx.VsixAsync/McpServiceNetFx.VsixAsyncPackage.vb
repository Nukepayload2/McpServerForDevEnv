Imports System
Imports System.Runtime.InteropServices
Imports System.Threading
Imports Microsoft.VisualStudio.Shell
Imports Microsoft.VisualStudio.Shell.Interop
Imports System.Threading.Tasks
Imports McpServiceNetFx.VsixAsync.ToolWindows

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
<InstalledProductRegistration("#110", "#112", "1.0", IconResourceID:=400)>
<ProvideMenuResource("Menus.ctmenu", 1)>
<ProvideToolWindow(GetType(ToolWindows.McpToolWindow), Style:=VsDockStyle.Tabbed, DockedWidth:=350, Window:="DocumentWell", Orientation:=ToolWindowOrientation.Left)>
<Guid(McpServiceNetFx.PackageGuidString)>
Public NotInheritable Class McpServiceNetFx
    Inherits AsyncPackage

    ''' <summary>
    ''' Package guid
    ''' </summary>
    Public Const PackageGuidString As String = "0e98d43d-5e0d-4bab-9224-33788cbe6a88"

    Private _mcpWindowState As ToolWindows.McpWindowState
    Private _isDisposed As Boolean = False

#Region "Package Members"

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
        Await JoinableTaskFactory.SwitchToMainThreadAsync()

        ' 初始化 MCP 窗口状态
        _mcpWindowState = New ToolWindows.McpWindowState(Me)

        ' 初始化我们的命令
        Await Commands.ShowMcpToolWindowCommand.InitializeAsync(Me)

        ' 记录包初始化日志
        _mcpWindowState.LogInfo("Package", "MCP Service NetFx VSIX 包已初始化")
    End Function

    Public Overrides Function GetAsyncToolWindowFactory(toolWindowType As Guid) As IVsAsyncToolWindowFactory
        Return If(toolWindowType.Equals(Guid.Parse(ToolWindows.McpToolWindow.WindowGuidString)), Me, Nothing)
    End Function

    Protected Overrides Async Function InitializeToolWindowAsync(toolWindowType As Type, id As Integer, cancellationToken As CancellationToken) As Task(Of Object)
        Await JoinableTaskFactory.SwitchToMainThreadAsync()

        If toolWindowType.Equals(GetType(ToolWindows.McpToolWindow)) Then
            Return New McpWindowState(Me)
        End If

        Return Nothing
    End Function

    Protected Overrides Sub Dispose(disposing As Boolean)
        If Not _isDisposed AndAlso disposing Then
            If _mcpWindowState IsNot Nothing Then
                _mcpWindowState.LogInfo("Package", "MCP Service NetFx VSIX 包正在关闭")
                _mcpWindowState.StopService()
            End If
            _isDisposed = True
        End If
        MyBase.Dispose(disposing)
    End Sub

#End Region

End Class
