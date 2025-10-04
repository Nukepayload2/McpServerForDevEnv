Imports System.Collections.Concurrent

''' <summary>
''' Visual Studio MCP 工具管理器
''' 负责管理所有工具的注册、发现和调用
''' </summary>
Public Class VisualStudioToolManager
    Private ReadOnly _tools As ConcurrentDictionary(Of String, VisualStudioToolBase)
    Private ReadOnly _logger As IMcpLogger
    Private ReadOnly _vsTools As VisualStudioTools
    Private ReadOnly _permissionHandler As IMcpPermissionHandler

    Public Sub New(logger As IMcpLogger, vsTools As VisualStudioTools, permissionHandler As IMcpPermissionHandler)
        _logger = logger
        _vsTools = vsTools
        _permissionHandler = permissionHandler
        _tools = New ConcurrentDictionary(Of String, VisualStudioToolBase)()

        RegisterAllTools()
    End Sub

    ''' <summary>
    ''' 注册所有工具
    ''' </summary>
    Private Sub RegisterAllTools()
        Try
            ' 手动注册所有工具，避免反射的性能开销
            RegisterTool(New BuildSolutionTool(_logger, _vsTools, _permissionHandler))
            RegisterTool(New BuildProjectTool(_logger, _vsTools, _permissionHandler))
            RegisterTool(New GetErrorListTool(_logger, _vsTools, _permissionHandler))
            RegisterTool(New GetSolutionInfoTool(_logger, _vsTools, _permissionHandler))
            RegisterTool(New GetActiveDocumentTool(_logger, _vsTools, _permissionHandler))

            ' 验证所有注册的工具都在工具注册表中
            ValidateToolRegistrations()

            _logger?.LogMcpRequest("工具管理器", "初始化完成", $"共注册 {_tools.Count} 个工具")

        Catch ex As Exception
            _logger?.LogMcpRequest("工具管理器", "初始化失败", ex.Message)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' 验证工具注册与工具注册表的一致性
    ''' </summary>
    Private Sub ValidateToolRegistrations()
        For Each tool In _tools.Values
            If Not ToolRegistry.IsKnownTool(tool.ToolName) Then
                _logger?.LogMcpRequest("工具验证", "警告", $"工具 {tool.ToolName} 未在工具注册表中定义")
            End If

            Dim expectedPermission = ToolRegistry.GetDefaultPermission(tool.ToolName)
            If tool.DefaultPermission <> expectedPermission Then
                _logger?.LogMcpRequest("工具验证", "警告", $"工具 {tool.ToolName} 的默认权限不匹配")
            End If
        Next

        ' 检查工具注册表中是否有未实现的工具
        For Each knownTool In ToolRegistry.KnownTools
            If Not _tools.ContainsKey(knownTool.Item1) Then
                _logger?.LogMcpRequest("工具验证", "警告", $"工具 {knownTool.Item1} 在注册表中定义但未实现")
            End If
        Next
    End Sub

    ''' <summary>
    ''' 获取所有工具定义
    ''' </summary>
    ''' <returns>工具定义列表</returns>
    Public Function GetToolDefinitions() As ToolDefinition()
        Return _tools.Values.Select(Function(t) t.ToolDefinition).ToArray()
    End Function

    ''' <summary>
    ''' 获取所有默认权限配置
    ''' </summary>
    ''' <returns>权限配置列表</returns>
    Public Function GetDefaultPermissions() As List(Of PersistenceModule.FeaturePermission)
        Return _tools.Values.Select(Function(t) New PersistenceModule.FeaturePermission With {
            .FeatureName = t.FeatureName,
            .Description = t.ToolDescription,
            .Permission = t.DefaultPermission
        }).ToList()
    End Function

    ''' <summary>
    ''' 执行指定工具
    ''' </summary>
    ''' <param name="toolName">工具名称</param>
    ''' <param name="arguments">工具参数</param>
    ''' <returns>执行结果</returns>
    Public Async Function ExecuteToolAsync(toolName As String, arguments As Dictionary(Of String, Object)) As Task(Of Object)
        If Not _tools.ContainsKey(toolName) Then
            Throw New McpException($"未找到工具: {toolName}", McpErrorCode.InvalidParams)
        End If

        Dim tool = _tools(toolName)

        _logger?.LogMcpRequest("工具执行", "开始", $"执行工具: {toolName}")

        Try
            Dim result = Await tool.ExecuteAsync(arguments)
            _logger?.LogMcpRequest("工具执行", "成功", $"工具 {toolName} 执行成功")
            Return result
        Catch ex As Exception
            _logger?.LogMcpRequest("工具执行", "失败", $"工具 {toolName} 执行失败: {ex.Message}")
            Throw
        End Try
    End Function

    ''' <summary>
    ''' 检查工具是否存在
    ''' </summary>
    ''' <param name="toolName">工具名称</param>
    ''' <returns>是否存在</returns>
    Public Function HasTool(toolName As String) As Boolean
        Return _tools.ContainsKey(toolName)
    End Function

    ''' <summary>
    ''' 获取已注册的工具数量
    ''' </summary>
    ''' <returns>工具数量</returns>
    Public Function GetToolCount() As Integer
        Return _tools.Count
    End Function

    ''' <summary>
    ''' 获取所有已注册的工具名称
    ''' </summary>
    ''' <returns>工具名称列表</returns>
    Public Function GetToolNames() As String()
        Return _tools.Keys.ToArray()
    End Function

    ''' <summary>
    ''' 手动注册工具（用于测试或扩展）
    ''' </summary>
    ''' <param name="tool">工具实例</param>
    Public Sub RegisterTool(tool As VisualStudioToolBase)
        If tool Is Nothing Then
            Throw New ArgumentException("工具不能为空", NameOf(tool))
        End If

        If _tools.TryAdd(tool.ToolName, tool) Then
            _logger?.LogMcpRequest("工具注册", "成功", $"手动注册工具: {tool.ToolName}")
        Else
            _logger?.LogMcpRequest("工具注册", "失败", $"工具 {tool.ToolName} 已存在")
        End If
    End Sub

    ''' <summary>
    ''' 注销工具
    ''' </summary>
    ''' <param name="toolName">工具名称</param>
    ''' <returns>是否成功注销</returns>
    Public Function UnregisterTool(toolName As String) As Boolean
        Dim removed As VisualStudioToolBase = Nothing
        If _tools.TryRemove(toolName, removed) Then
            _logger?.LogMcpRequest("工具注销", "成功", $"已注销工具: {toolName}")
            Return True
        Else
            _logger?.LogMcpRequest("工具注销", "失败", $"工具 {toolName} 不存在")
            Return False
        End If
    End Function
End Class