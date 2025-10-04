''' <summary>
''' 工具注册表 - 提供所有可用工具的静态信息
''' 用于权限系统和工具管理器的统一数据源
''' </summary>
Public Module ToolRegistry
    ''' <summary>
    ''' 所有已知的工具定义
    ''' </summary>
    Public ReadOnly Property KnownTools As List(Of (String, String, PermissionLevel)) = New List(Of (String, String, PermissionLevel)) From {
        ("build_solution", "构建整个解决方案", PermissionLevel.Ask),
        ("build_project", "构建指定项目", PermissionLevel.Ask),
        ("get_error_list", "获取当前的错误和警告列表", PermissionLevel.Allow),
        ("get_solution_info", "获取当前解决方案信息", PermissionLevel.Allow),
        ("get_active_document", "获取当前活动文档的路径", PermissionLevel.Allow)
    }

    ''' <summary>
    ''' 获取所有工具的默认权限配置
    ''' </summary>
    ''' <returns>权限配置列表</returns>
    Public Function GetDefaultPermissions() As List(Of FeaturePermission)
        Return KnownTools.Select(Function(t) New FeaturePermission With {
            .FeatureName = t.Item1,
            .Description = t.Item2,
            .Permission = t.Item3
        }).ToList()
    End Function

    ''' <summary>
    ''' 检查是否为已知的工具
    ''' </summary>
    ''' <param name="toolName">工具名称</param>
    ''' <returns>是否为已知工具</returns>
    Public Function IsKnownTool(toolName As String) As Boolean
        Return KnownTools.Any(Function(t) t.Item1 = toolName)
    End Function

    ''' <summary>
    ''' 获取工具的默认权限
    ''' </summary>
    ''' <param name="toolName">工具名称</param>
    ''' <returns>默认权限级别</returns>
    Public Function GetDefaultPermission(toolName As String) As PermissionLevel
        Dim tool = KnownTools.FirstOrDefault(Function(t) t.Item1 = toolName)
        If tool.Item1 IsNot Nothing Then
            Return tool.Item3
        End If
        Return PermissionLevel.Ask ' 默认为询问
    End Function

    ''' <summary>
    ''' 获取工具描述
    ''' </summary>
    ''' <param name="toolName">工具名称</param>
    ''' <returns>工具描述</returns>
    Public Function GetToolDescription(toolName As String) As String
        Dim tool = KnownTools.FirstOrDefault(Function(t) t.Item1 = toolName)
        If tool.Item1 IsNot Nothing Then
            Return tool.Item2
        End If
        Return "未知工具"
    End Function

    ''' <summary>
    ''' 添加新工具到注册表
    ''' </summary>
    ''' <param name="toolName">工具名称</param>
    ''' <param name="description">工具描述</param>
    ''' <param name="defaultPermission">默认权限</param>
    Public Sub RegisterTool(toolName As String, description As String, defaultPermission As PermissionLevel)
        ' 检查是否已存在
        Dim existingTool = KnownTools.FirstOrDefault(Function(t) t.Item1 = toolName)
        If existingTool.Item1 Is Nothing Then
            KnownTools.Add((toolName, description, defaultPermission))
        Else
            ' 更新现有工具
            Dim index = KnownTools.IndexOf(existingTool)
            KnownTools(index) = (toolName, description, defaultPermission)
        End If
    End Sub
End Module