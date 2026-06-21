Imports System.Threading
Imports System.Threading.Tasks
Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

' WPF 调试工具的中间基类（#23）。
'
' 【职责】统一持有 #22 已加进 VisualStudioToolBase 的 _wpfDebugProxy / IsWpfDebugConnected，
' 把「未连接检查 + SendRequest + GetPayload 解析」这条主链路抽成受保护的辅助方法，
' 让六个具体工具（list_windows/take_snapshot/click/fill/evaluate/take_screenshot）只关心
' 自己的参数构造和返回值格式化，不再各自重写样板。
'
' 【和 VisualStudioToolBase 的分工】
' - VS 工具（GetSolutionInfoTool 等）走 _vsTools + HasDataContext 检查。
' - WPF 工具走 _wpfDebugProxy + IsWpfDebugConnected 检查；内部绝不碰 _vsTools（被控端没有 DTE）。
'   注意：基类 VisualStudioToolBase.ExecuteAsync 仍会检查 HasDataContext（_vsTools 非空）。
'   WPF 工具的 _vsTools 由工具管理器在 CreateVsTools 时一并注入（哪怕该工具用不到 VS），
'   因此 HasDataContext 判断对 WPF 工具天然成立，不会误拦——这条等价于「VS 实例必须先选过」，
'   与 rough-plan 第 117 行「WPF 调试 tab 在主窗口里、选 VS 实例后才出现」一致。
'
' 【错误风格】未连接被控端时报「未连接被控端，请先在 WPF 调试 tab 连接」，沿用现有 VS 工具
' 「未初始化」的 McpException + McpErrorCode.InternalError 风格（见 VisualStudioToolBase.ExecuteAsync）。

''' <summary>
''' WPF 调试 MCP 工具的中间基类。统一封装「未连接检查 + 发请求 + 解析业务返回值」主链路。
''' </summary>
''' <remarks>
''' 具体工具继承本类，重写 <see cref="BuildParams"/>（把 MCP 入参翻译成被控端参数对象）和
''' <see cref="FormatResult"/>（把被控端业务返回值格式化成 MCP 返回值），其余主链路由本类接管。
''' </remarks>
Public MustInherit Class WpfDebugToolBase
    Inherits VisualStudioToolBase

    ''' <summary>调用被控端的超时（毫秒）。被控端卡死时主控不至于永远挂起。</summary>
    Private Const DefaultRequestTimeoutMs As Integer = 30000

    ''' <summary>
    ''' 创建工具实例。
    ''' </summary>
    Protected Sub New(logger As IMcpLogger, permissionHandler As IMcpPermissionHandler)
        MyBase.New(logger, permissionHandler)
    End Sub

    ''' <summary>
    ''' 本工具调用的被控端 Method 名（取 <see cref="WpfDebugMethods"/> 常量）。
    ''' 具体工具返回对应常量，主链路用它发请求。
    ''' </summary>
    Public MustOverride ReadOnly Property Method As String

    ''' <summary>
    ''' 把 MCP 入参翻译成被控端参数对象（将被 JObject.FromObject 包成 request.params）。
    ''' 无参方法返回 Nothing；具体工具在此做参数校验（沿用基类 ValidateRequiredArguments 风格）。
    ''' </summary>
    ''' <param name="arguments">MCP 工具入参字典。</param>
    ''' <returns>参数对象，或 Nothing 表示无参。</returns>
    Protected MustOverride Function BuildParams(arguments As Dictionary(Of String, Object)) As Object

    ''' <summary>
    ''' 把被控端业务返回值（JToken，已由 <see cref="WpfDebugResultReader.GetPayload"/> 解出嵌套层）
    ''' 格式化成 MCP 工具返回值。具体工具按自身契约决定返回字符串、对象还是结构化数据。
    ''' </summary>
    ''' <param name="payload">业务返回值；Sub 类方法（如 click/fill）时为 Nothing。</param>
    Protected MustOverride Function FormatResult(payload As JToken) As Object

    ''' <summary>
    ''' 主链路：未连接检查 → 权限 → BuildParams → SendRequest → GetPayload → FormatResult。
    ''' 全程异步，不调 _vsTools，调 _wpfDebugProxy。
    ''' </summary>
    Protected Overrides Async Function ExecuteInternalAsync(arguments As Dictionary(Of String, Object)) As Task(Of Object)
        Try
            ' 未连接被控端：沿用现有 VS 工具「未初始化」的错误返回风格。
            If Not IsWpfDebugConnected Then
                Throw New McpException(
                    "未连接被控端，请先在 WPF 调试 tab 连接",
                    McpErrorCode.InternalError)
            End If

            ' 权限检查沿用基类风格（与 GetSolutionInfoTool 一致）。
            If Not CheckPermission() Then
                Throw New McpException("权限被拒绝", McpErrorCode.InvalidParams)
            End If

            ' 参数构造（具体工具在此做校验）。
            Dim methodParams As Object = BuildParams(arguments)

            LogOperation(ToolName, "开始", $"调用被控端方法: {Method}")

            ' 发请求 + 取业务返回值。带超时取消令牌，避免被控端卡死时主控挂死。
            Dim payload As JToken
            Using cts As New CancellationTokenSource(DefaultRequestTimeoutMs)
                Dim response As WpfDebugResponse = Await _wpfDebugProxy.SendRequestAsync(
                    Method, methodParams, cts.Token).ConfigureAwait(False)
                payload = WpfDebugResultReader.GetPayload(response)
            End Using

            Dim result As Object = FormatResult(payload)

            LogOperation(ToolName, "完成", $"被控端方法 {Method} 执行成功")

            Return result

        Catch ex As McpException
            LogOperation(ToolName, "失败", ex.Message)
            Throw
        Catch ex As WpfDebugRemoteException
            ' 被控端报错（业务层失败，如 uid 失效）：转成 McpException，沿用「InternalError + 被控端报错」风格。
            LogOperation(ToolName, "失败", $"被控端报错: {ex.Message}")
            Throw New McpException($"被控端执行失败: {ex.Message}", McpErrorCode.InternalError)
        Catch ex As Exception
            LogOperation(ToolName, "失败", ex.Message)
            Throw New McpException($"WPF 调试工具执行失败: {ex.Message}", McpErrorCode.InternalError)
        End Try
    End Function

    ''' <summary>
    ''' 取可选字符串参数；为空字符串视同未提供（返回 Nothing）。
    ''' 用于 windowId?/uid? 这类「空即不指定」的可选参数。
    ''' </summary>
    Protected Function GetOptionalStringOrNothing(arguments As Dictionary(Of String, Object), paramName As String) As String
        ' 显式指定 String 类型实参：GetOptionalArgument(Of T) 无法从 defaultValue:=Nothing 推断 T。
        Dim value As String = GetOptionalArgument(Of String)(arguments, paramName, Nothing)
        If String.IsNullOrEmpty(value) Then Return Nothing
        Return value
    End Function
End Class
