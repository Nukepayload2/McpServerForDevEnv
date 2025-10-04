Public Interface IMcpPermissionHandler
    Function CheckPermission(featureName As String, operationDescription As String) As Boolean
End Interface