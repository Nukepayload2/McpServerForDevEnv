Imports System
Imports System.ComponentModel.Design
Imports System.Threading
Imports Microsoft.VisualStudio.Shell
Imports Microsoft.VisualStudio.Shell.Interop
Imports Task = System.Threading.Tasks.Task

Namespace Commands
    ''' <summary>
    ''' Command handler for showing the MCP Tool Window
    ''' </summary>
    Friend NotInheritable Class ShowMcpToolWindowCommand
        Public Shared Async Function InitializeAsync(package As AsyncPackage) As Task
            Await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken)

            Dim commandService As IMenuCommandService = Await package.GetServiceAsync(GetType(IMenuCommandService))
            If commandService IsNot Nothing Then
                Dim menuCommandID As New CommandID(Guid.Parse("9cc1062b-4c82-46d2-adcb-f5c17d55fb86"), &H0100)
                Dim menuItem As New MenuCommand(Sub(sender, e) Execute(package), menuCommandID)
                commandService.AddCommand(menuItem)
            End If
        End Function

        Private Shared Async Sub Execute(package As AsyncPackage)
            Await package.JoinableTaskFactory.RunAsync(
            Async Function()
                Await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync()

                Dim window = Await package.ShowToolWindowAsync(
                    GetType(ToolWindows.McpToolWindow),
                     0,
                    create:=True,
                    cancellationToken:=package.DisposalToken)

                If window Is Nothing Then
                    Throw New Exception("无法创建MCP工具窗口")
                End If
            End Function)
        End Sub
    End Class
End Namespace