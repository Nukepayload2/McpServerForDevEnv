Imports System.Windows.Input

''' <summary>
''' 中继命令类
''' </summary>
Public Class RelayCommand
    Implements ICommand

    Private ReadOnly _execute As Action
    Private ReadOnly _canExecute As Func(Of Boolean)

    Public Sub New(execute As Action, Optional canExecute As Func(Of Boolean) = Nothing)
        _execute = execute
        _canExecute = canExecute
    End Sub

    Public Sub New(execute As Func(Of Task), Optional canExecute As Func(Of Boolean) = Nothing)
        _execute = Sub()
                       Dim task As Task = execute()
                       task.ConfigureAwait(False).GetAwaiter().GetResult()
                   End Sub
        _canExecute = canExecute
    End Sub

    Public Event CanExecuteChanged As EventHandler Implements ICommand.CanExecuteChanged

    Public Function CanExecute(parameter As Object) As Boolean Implements ICommand.CanExecute
        Return If(_canExecute Is Nothing, True, _canExecute())
    End Function

    Public Sub Execute(parameter As Object) Implements ICommand.Execute
        _execute()
    End Sub

    Public Sub RaiseCanExecuteChanged()
        RaiseEvent CanExecuteChanged(Me, EventArgs.Empty)
    End Sub
End Class