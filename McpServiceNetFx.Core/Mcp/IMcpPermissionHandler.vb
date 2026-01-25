''' <summary>
''' MCP 权限处理器接口
''' </summary>
Public Interface IMcpPermissionHandler
    ''' <summary>
    ''' 检查功能权限
    ''' </summary>
    ''' <param name="featureName">功能名称</param>
    ''' <param name="operationDescription">操作描述</param>
    ''' <returns>是否有权限</returns>
    Function CheckPermission(featureName As String, operationDescription As String) As Boolean

    ''' <summary>
    ''' 检查文件权限（增强版本，支持路径策略）
    ''' </summary>
    ''' <param name="featureName">功能名称</param>
    ''' <param name="operationDescription">操作描述</param>
    ''' <param name="filePath">文件路径</param>
    ''' <param name="accessType">访问类型</param>
    ''' <returns>是否有权限</returns>
    Function CheckFilePermission(featureName As String, operationDescription As String, filePath As String, accessType As FileAccessType) As Boolean
End Interface
