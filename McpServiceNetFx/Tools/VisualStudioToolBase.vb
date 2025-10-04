''' <summary>
''' Visual Studio MCP 工具的抽象基类
''' 提供工具定义、权限检查和日志记录的通用功能
''' </summary>
Public MustInherit Class VisualStudioToolBase
    Protected ReadOnly _logger As IMcpLogger
    Protected _vsTools As VisualStudioTools ' 可以延迟设置
    Protected ReadOnly _permissionHandler As IMcpPermissionHandler

    ''' <summary>
    ''' 创建工具实例（延迟数据上下文版本）
    ''' </summary>
    Protected Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        _logger = logger
        _vsTools = Nothing ' 数据上下文将稍后设置
        _permissionHandler = permissionHandler
    End Sub

    ''' <summary>
    ''' 设置数据上下文
    ''' 在工具管理器创建数据上下文后调用
    ''' </summary>
    ''' <param name="vsTools">Visual Studio 工具实例</param>
    Public Sub SetVsTools(vsTools As VisualStudioTools)
        _vsTools = vsTools
    End Sub

    ''' <summary>
    ''' 检查数据上下文是否已设置
    ''' </summary>
    Protected ReadOnly Property HasDataContext As Boolean
        Get
            Return _vsTools IsNot Nothing
        End Get
    End Property

    ''' <summary>
    ''' 获取工具定义
    ''' </summary>
    Public MustOverride ReadOnly Property ToolDefinition As ToolDefinition

    ''' <summary>
    ''' 获取默认权限级别
    ''' </summary>
    Public MustOverride ReadOnly Property DefaultPermission As PermissionLevel

    ''' <summary>
    ''' 获取工具名称（从ToolDefinition获取）
    ''' </summary>
    Public ReadOnly Property ToolName As String
        Get
            Return ToolDefinition.Name
        End Get
    End Property

    ''' <summary>
    ''' 获取工具描述（从ToolDefinition获取）
    ''' </summary>
    Public ReadOnly Property ToolDescription As String
        Get
            Return ToolDefinition.Description
        End Get
    End Property

    ''' <summary>
    ''' 获取功能名称（用于权限检查）
    ''' </summary>
    Public Overridable ReadOnly Property FeatureName As String
        Get
            Return ToolName
        End Get
    End Property

    ''' <summary>
    ''' 执行工具（包装方法）
    ''' </summary>
    ''' <param name="arguments">工具参数</param>
    ''' <returns>执行结果</returns>
    Public Async Function ExecuteAsync(arguments As Dictionary(Of String, Object)) As Task(Of Object)
        Try
            ' 检查数据上下文
            If Not HasDataContext Then
                Throw New McpException("工具数据上下文未设置，无法执行工具", McpErrorCode.InternalError)
            End If

            ' 调用具体的执行实现
            Return Await ExecuteInternalAsync(arguments)
        Catch ex As Exception
            If TypeOf ex Is McpException Then
                Throw
            Else
                LogOperation(ToolName, "执行异常", ex.Message)
                Throw New McpException($"工具执行失败: {ex.Message}", McpErrorCode.InternalError)
            End If
        End Try
    End Function

    ''' <summary>
    ''' 执行工具的具体实现
    ''' 子类需要重写此方法
    ''' </summary>
    ''' <param name="arguments">工具参数</param>
    ''' <returns>执行结果</returns>
    Protected MustOverride Async Function ExecuteInternalAsync(arguments As Dictionary(Of String, Object)) As Task(Of Object)

    ''' <summary>
    ''' 检查权限
    ''' </summary>
    ''' <returns>是否有权限</returns>
    Protected Function CheckPermission() As Boolean
        Try
            Return _permissionHandler.CheckPermission(FeatureName, ToolDescription)
        Catch ex As Exception
            _logger?.LogMcpRequest("权限检查", "异常", ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 记录工具操作日志
    ''' </summary>
    ''' <param name="action">操作名称</param>
    ''' <param name="result">结果</param>
    ''' <param name="details">详细信息</param>
    Protected Sub LogOperation(action As String, result As String, details As String)
        _logger?.LogMcpRequest(action, result, details)
    End Sub

    ''' <summary>
    ''' 验证必需参数
    ''' </summary>
    ''' <param name="arguments">参数字典</param>
    ''' <param name="requiredParams">必需参数列表</param>
    ''' <exception cref="McpException">缺少必需参数时抛出</exception>
    Protected Sub ValidateRequiredArguments(arguments As Dictionary(Of String, Object), ParamArray requiredParams As String())
        If arguments Is Nothing Then
            Throw New McpException("参数不能为空", McpErrorCode.InvalidParams)
        End If

        For Each param In requiredParams
            If Not arguments.ContainsKey(param) OrElse arguments(param) Is Nothing Then
                Throw New McpException($"缺少必需参数: {param}", McpErrorCode.InvalidParams)
            End If
        Next
    End Sub

    ''' <summary>
    ''' 获取可选参数值
    ''' </summary>
    ''' <typeparam name="T">参数类型</typeparam>
    ''' <param name="arguments">参数字典</param>
    ''' <param name="paramName">参数名称</param>
    ''' <param name="defaultValue">默认值</param>
    ''' <returns>参数值</returns>
    Protected Function GetOptionalArgument(Of T)(arguments As Dictionary(Of String, Object), paramName As String, defaultValue As T) As T
        If arguments IsNot Nothing AndAlso arguments.ContainsKey(paramName) AndAlso arguments(paramName) IsNot Nothing Then
            Try
                Return CType(Convert.ChangeType(arguments(paramName), GetType(T)), T)
            Catch ex As Exception
                LogOperation(ToolName, "参数转换失败", $"参数 {paramName} 转换失败: {ex.Message}")
                Return defaultValue
            End Try
        End If
        Return defaultValue
    End Function
End Class