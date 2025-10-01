Imports System
Imports System.ComponentModel.Design
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.VisualStudio.Shell
Imports Microsoft.VisualStudio.Shell.Interop

''' <summary>
''' 符号搜索命令
''' </summary>
Friend NotInheritable Class SymbolSearchCommand
    Public Shared ReadOnly CommandId As New CommandID(Guid.Parse("e86b2aef-5348-4365-915b-29966ce14d78"), &H100)

    Private Shared _command As MenuCommand

    ''' <summary>
    ''' 初始化命令
    ''' </summary>
    Public Shared Async Function InitializeAsync(package As AsyncPackage) As Task
        Await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken)

        Dim commandService As IMenuCommandService = Await package.GetServiceAsync(GetType(IMenuCommandService))
        If commandService IsNot Nothing Then
            _command = New MenuCommand(AddressOf Execute, CommandId)
            commandService.AddCommand(_command)
        End If
    End Function

    ''' <summary>
    ''' 执行命令
    ''' </summary>
    Private Shared Async Sub Execute(sender As Object, e As EventArgs)
        Await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync()

        Dim package As AsyncPackage = TryCast(sender, AsyncPackage)
        If package Is Nothing Then Return

        ' 显示工具窗口
        Dim window = Await package.ShowToolWindowAsync(
            GetType(SymbolSearchToolWindow),
            0,
            create:=True,
            cancellationToken:=package.DisposalToken)

        If window Is Nothing Then
            Throw New InvalidOperationException("无法创建符号搜索工具窗口")
        End If

        Dim frame As IVsWindowFrame = TryCast(window.Frame, IVsWindowFrame)
        If frame Is Nothing Then
            Throw New InvalidOperationException("无法获取工具窗口框架")
        End If

        ' 显示并聚焦工具窗口
        frame.Show()
    End Sub
End Class
