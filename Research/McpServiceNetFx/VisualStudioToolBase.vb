Imports System.ComponentModel
Imports System.Reflection

''' <summary>
''' Visual Studio MCP 工具的抽象基类
''' 提供工具定义、权限检查和日志记录的通用功能
''' </summary>
Public MustInherit Class VisualStudioToolBase
    Protected ReadOnly _logger As IMcpLogger
    Protected ReadOnly _vsTools As VisualStudioTools
    Protected ReadOnly _permissionHandler As IMcpPermissionHandler

    Protected Sub New(logger As IMcpLogger, vsTools As VisualStudioTools, permissionHandler As IMcpPermissionHandler)
        _logger = logger
        _vsTools = vsTools
        _permissionHandler = permissionHandler
    End Sub

    ''' <summary>
    ''' 获取工具定义
    ''' </summary>
    Public MustOverride ReadOnly Property ToolDefinition As ToolDefinition

    ''' <summary>
    ''' 获取默认权限级别
    ''' </summary>
    Public MustOverride ReadOnly Property DefaultPermission As PersistenceModule.PermissionLevel

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
    ''' 执行工具
    ''' </summary>
    ''' <param name="arguments">工具参数</param>
    ''' <returns>执行结果</returns>
    Public MustOverride Async Function ExecuteAsync(arguments As Dictionary(Of String, Object)) As Task(Of Object)

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