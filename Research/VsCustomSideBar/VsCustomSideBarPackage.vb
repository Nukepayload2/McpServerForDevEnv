Imports System
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.VisualStudio.Shell
Imports Microsoft.VisualStudio.Shell.Interop

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
<Guid(VsCustomSideBarPackage.PackageGuidString)>
Public NotInheritable Class VsCustomSideBarPackage
    Inherits AsyncPackage

    ''' <summary>
    ''' Package guid
    ''' </summary>
    Public Const PackageGuidString As String = "f9123147-d9c6-4873-8789-3f45ab42edcb"

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

        ' Initialize our command
        Await Commands.ShowMcpToolWindowCommand.InitializeAsync(Me)
    End Function

    Public Overrides Function GetAsyncToolWindowFactory(toolWindowType As Guid) As IVsAsyncToolWindowFactory
        Return If(toolWindowType.Equals(Guid.Parse(ToolWindows.McpToolWindow.WindowGuidString)), Me, Nothing)
    End Function

    Protected Overrides Async Function InitializeToolWindowAsync(toolWindowType As Type, id As Integer, cancellationToken As CancellationToken) As Task(Of Object)
        Await JoinableTaskFactory.SwitchToMainThreadAsync()

        If toolWindowType.Equals(GetType(ToolWindows.McpToolWindow)) Then
            Return New ToolWindows.McpWindowState()
        End If

        Return Nothing
    End Function

#End Region

End Class
