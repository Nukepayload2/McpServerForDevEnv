Imports System.Collections.Concurrent

''' <summary>
''' Visual Studio MCP 工具管理器
''' 负责管理所有工具的注册、发现和调用
''' </summary>
Public Class VisualStudioToolManager
    Private ReadOnly _tools As ConcurrentDictionary(Of String, VisualStudioToolBase)
    Private ReadOnly _logger As IMcpLogger
    Private _vsTools As VisualStudioTools
    Private ReadOnly _permissionHandler As IMcpPermissionHandler
    Private _isInitialized As Boolean = False
    Private _currentDte As EnvDTE80.DTE2

    ''' <summary>
    ''' 创建工具管理器实例（应用启动时使用）
    ''' 创建框架并注册所有工具，但数据上下文延迟创建
    ''' </summary>
    Public Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        _logger = logger
        _permissionHandler = permissionHandler
        _tools = New ConcurrentDictionary(Of String, VisualStudioToolBase)()
        _vsTools = Nothing ' 数据上下文将在稍后创建
        _isInitialized = False

        ' 立即注册工具，但不传入数据上下文
        RegisterAllToolsWithoutContext()

        _logger?.LogMcpRequest("工具管理器", "创建框架", $"工具管理器已创建，已注册 {_tools.Count} 个工具，等待数据上下文")
    End Sub

    ''' <summary>
    ''' 创建数据上下文并设置给所有已注册的工具
    ''' 在选择 Visual Studio 实例后调用
    ''' </summary>
    ''' <param name="dte2">Visual Studio DTE2 实例</param>
    ''' <param name="dispatcher">UI 线程调度器</param>
    Public Sub CreateVsTools(dte2 As EnvDTE80.DTE2, dispatcher As IDispatcher)
        Try
            ' 如果已使用相同 DTE 初始化，跳过
            If _isInitialized AndAlso _currentDte IsNot Nothing AndAlso _currentDte Is dte2 Then
                _logger?.LogMcpRequest("工具管理器", "数据上下文", "工具管理器已使用相同 DTE 初始化，跳过重复创建")
                Return
            End If

            _vsTools = New VisualStudioTools(dte2, dispatcher, _logger)

            ' 为所有已注册的工具设置数据上下文
            For Each tool In _tools.Values
                tool.SetVsTools(_vsTools)
            Next

            _currentDte = dte2
            _isInitialized = True
            _logger?.LogMcpRequest("工具管理器", "数据上下文创建完成", $"Visual Studio 实例: {dte2.Name}, 工具数量: {_tools.Count}")

        Catch ex As Exception
            _logger?.LogMcpRequest("工具管理器", "数据上下文创建失败", ex.Message)
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' 注册所有工具（无数据上下文版本）
    ''' 在应用启动时调用，工具将在数据上下文创建后才能执行
    ''' </summary>
    Private Sub RegisterAllToolsWithoutContext()
        Try
            ' 手动注册所有工具，避免反射的性能开销
            ' 使用延迟数据上下文构造函数

            ' Visual Studio 相关工具
            RegisterTool(New BuildSolutionTool(_logger, _permissionHandler))
            RegisterTool(New BuildProjectTool(_logger, _permissionHandler))
            RegisterTool(New RunProjectCustomToolsTool(_logger, _permissionHandler))
            RegisterTool(New GetErrorListTool(_logger, _permissionHandler))
            RegisterTool(New GetSolutionInfoTool(_logger, _permissionHandler))
            RegisterTool(New GetActiveDocumentTool(_logger, _permissionHandler))
            RegisterTool(New GetAllOpenDocumentsTool(_logger, _permissionHandler))

            ' 文件操作工具
            RegisterTool(New ReadLinesTool(_logger, _permissionHandler, 0))
            RegisterTool(New ReadLinesTool(_logger, _permissionHandler, 1))
            RegisterTool(New ReplaceLinesTool(_logger, _permissionHandler, 0))
            RegisterTool(New ReplaceLinesTool(_logger, _permissionHandler, 1))
            RegisterTool(New WriteLinesTool(_logger, _permissionHandler))
            RegisterTool(New AppendLinesTool(_logger, _permissionHandler))
            RegisterTool(New StringReplaceTool(_logger, _permissionHandler))

            ' 文件搜索工具
            RegisterTool(New FindFilesByNameTool(_logger, _permissionHandler))
            RegisterTool(New FindFilesByTextContentTool(_logger, _permissionHandler))

            ' WPF 调试工具（#23，常驻注册）
            ' 设计见 rough-plan 第九节第 4 点：不追求「连上才出现」，六个工具始终在工具列表里；
            ' 没连被控端时调用按现有 VS 工具「未初始化」风格报「未连接被控端」（WpfDebugToolBase 主链路处理）。
            ' 这里只 new + 注册，不注入 proxy——proxy 在 WPF 调试 tab 连上后由
            ' CreateWpfDebugTools 经 SetWpfDebugProxy 注入；未注入时 IsWpfDebugConnected 为 False。
            RegisterTool(New ListWindowsTool(_logger, _permissionHandler))
            RegisterTool(New TakeSnapshotTool(_logger, _permissionHandler))
            RegisterTool(New ClickTool(_logger, _permissionHandler))
            RegisterTool(New FillTool(_logger, _permissionHandler))
            RegisterTool(New EvaluateTool(_logger, _permissionHandler))
            RegisterTool(New TakeScreenshotTool(_logger, _permissionHandler))

            _logger?.LogMcpRequest("工具管理器", "工具预注册完成", $"共预注册 {_tools.Count} 个工具，等待数据上下文")

        Catch ex As Exception
            _logger?.LogMcpRequest("工具管理器", "工具预注册失败", ex.Message)
            Throw
        End Try
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
    Public Function GetDefaultPermissions() As List(Of PermissionItem)
        ' 即使数据上下文未创建，工具也已预注册，可以获取权限配置
        Return _tools.Values.Select(Function(t) New PermissionItem With {
            .FeatureName = t.FeatureName,
            .Description = t.ToolDescription,
            .Permission = t.DefaultPermission,
            .IsFileTool = t.IsFileTool
        }).ToList()
    End Function

    ''' <summary>
    ''' 检查工具管理器是否已初始化
    ''' </summary>
    Public ReadOnly Property IsInitialized As Boolean
        Get
            Return _isInitialized
        End Get
    End Property

    ''' <summary>
    ''' 执行指定工具
    ''' </summary>
    ''' <param name="toolName">工具名称</param>
    ''' <param name="arguments">工具参数</param>
    ''' <returns>执行结果</returns>
    Public Async Function ExecuteToolAsync(toolName As String, arguments As Dictionary(Of String, Object)) As Task(Of Object)
        ' 不在此处检查 _isInitialized：服务支持无 VS 实例启动（_isInitialized 仅表示 VS 数据上下文已注入，
        ' 工具本身在构造时已注册）。未就绪由各工具自行容错——VS 工具查 HasDataContext、WPF 工具查
        ' IsWpfDebugConnected，返回准确简短的错误（如"未选择 VS 实例"/"未连接 WPF 被控端"）。
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
    ''' 创建 WPF 调试数据上下文并注入给所有已注册工具（DC 注入扩展点，#22）。
    ''' 照搬 CreateVsTools 的"注入 DC 给所有工具"模式：WPF 调试 tab 连上被控端后调用，
    ''' 把当前 proxy 经 SetWpfDebugProxy 设置给每个工具。
    ''' <para>
    ''' 注意：WPF 调试工具常驻注册（rough-plan 第九节第 4 点），六个具体工具
    ''' （list_windows/take_snapshot/click/fill/evaluate/take_screenshot）已在
    ''' <see cref="RegisterAllToolsWithoutContext"/> 里随管理器构造时注册。本方法不 RegisterTool，
    ''' 只负责把 proxy 注入给所有已注册工具——六工具据此判断 IsWpfDebugConnected，
    ''' 调用时若未注入（未连接被控端）由 WpfDebugToolBase 主链路报「未连接被控端」。
    ''' </para>
    ''' </summary>
    ''' <param name="proxy">当前 WPF 调试被控端代理（连接成功后传入）。</param>
    ''' <param name="dispatcher">UI 线程调度器（与 CreateVsTools 对齐，#23 工具执行时按需用）。</param>
    Public Sub CreateWpfDebugTools(proxy As WpfDebugProxy, dispatcher As IDispatcher)
        If proxy Is Nothing Then Throw New ArgumentNullException(NameOf(proxy))

        For Each tool In _tools.Values
            tool.SetWpfDebugProxy(proxy)
        Next

        _logger?.LogMcpRequest("工具管理器", "WPF 调试 DC 注入", $"已为 {_tools.Count} 个工具注入 WPF 调试 proxy")
    End Sub

    ''' <summary>
    ''' 清除所有工具的 WPF 调试 proxy 注入（断开被控端时调用）。
    ''' 之后 IsWpfDebugConnected 判断回到 False，WPF 工具调用报"未连接被控端"。
    ''' </summary>
    Public Sub ClearWpfDebugProxy()
        For Each tool In _tools.Values
            tool.SetWpfDebugProxy(Nothing)
        Next

        _logger?.LogMcpRequest("工具管理器", "WPF 调试 DC 清除", "已清除所有工具的 WPF 调试 proxy")
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