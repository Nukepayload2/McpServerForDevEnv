Public Interface IMcpLogger
    Sub LogServiceAction(action As String, result As String, details As String)
    Sub LogMcpRequest(operation As String, result As String, details As String)
End Interface