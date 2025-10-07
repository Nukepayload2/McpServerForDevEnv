Public Interface IDispatcher
    Function InvokeAsync(job As Func(Of Task)) As Task
    Function InvokeAsync(job As Action) As Task
    Sub Invoke(job As Action)
End Interface

Public Interface IClipboard
    Sub SetText(text As String)
End Interface

Public Interface IInteraction
    Sub ShowCopyCommandDialog(title As String, message As String, command As String)
End Interface
