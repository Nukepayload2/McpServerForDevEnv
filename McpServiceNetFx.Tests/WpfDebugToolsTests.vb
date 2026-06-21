Imports System.Reflection
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServiceNetFx
Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

''' <summary>
''' WPF 调试 MVP 六工具（#23）的纯逻辑单测。
''' 无副作用：不开 pipe、不启进程、不碰文件、不碰 UI、不碰注册表。
'''
''' 【覆盖范围】
''' 1. 工具定义（公开属性）：Method 常量、DefaultPermission、inputSchema 字段——验证对外契约正确。
''' 2. 参数构造 BuildParams（protected，用反射访问）：无参/可选参数缺省/可选参数透传/必填校验/非法值容错。
''' 3. 响应格式化 FormatResult（protected，用反射访问）：Sub 方法成功确认、标量提取、数组/对象透传、Nothing 兜底。
''' 4. 快照文本渲染 SnapshotFormatter（公开静态方法，纯函数）：缩进、属性、嵌套、边界。
'''
''' 【未覆盖、留 #24 端到端联调】
''' - ExecuteInternalAsync 主链路（调 _wpfDebugProxy.SendRequestAsync，需真实 pipe 连接，有副作用）。
'''   未连接分支、被控端报错转 McpException、超时取消等行为在 #24 联真被控端时验证。
'''   该主链路逻辑已在 WpfDebugToolBase.ExecuteInternalAsync 里集中实现，#24 一次覆盖即可。
''' </summary>
<TestClass>
Public Class WpfDebugToolsTests

#Region "工具定义（公开属性，验证对外契约）"

    ''' <summary>list_windows：Method 常量、Allow 权限、无参 schema。</summary>
    <TestMethod>
    Public Sub ListWindows_Definition_NoParams_AllowPermission()
        Dim tool = MakeTool(Of ListWindowsTool)()

        Assert.AreEqual(WpfDebugMethods.ListWindows, tool.Method)
        Assert.AreEqual("list_windows", tool.ToolDefinition.Name)
        Assert.AreEqual(PermissionLevel.Allow, tool.DefaultPermission)
        Assert.IsNull(tool.ToolDefinition.InputSchema.Required)
        Assert.IsEmpty(tool.ToolDefinition.InputSchema.Properties)
    End Sub

    ''' <summary>take_snapshot：三个可选参数，无 required。</summary>
    <TestMethod>
    Public Sub TakeSnapshot_Definition_ThreeOptionalProps_NoRequired()
        Dim tool = MakeTool(Of TakeSnapshotTool)()

        Assert.AreEqual(WpfDebugMethods.TakeSnapshot, tool.Method)
        Assert.AreEqual("take_snapshot", tool.ToolDefinition.Name)
        Assert.AreEqual(PermissionLevel.Allow, tool.DefaultPermission)
        Assert.IsNull(tool.ToolDefinition.InputSchema.Required)
        Dim props = tool.ToolDefinition.InputSchema.Properties
        Assert.IsTrue(props.ContainsKey("windowId"))
        Assert.IsTrue(props.ContainsKey("maxDepth"))
        Assert.IsTrue(props.ContainsKey("interestingOnly"))
    End Sub

    ''' <summary>click：uid 必填。</summary>
    <TestMethod>
    Public Sub Click_Definition_UidRequired()
        Dim tool = MakeTool(Of ClickTool)()

        Assert.AreEqual(WpfDebugMethods.Click, tool.Method)
        Assert.AreEqual("click", tool.ToolDefinition.Name)
        CollectionAssert.Contains(tool.ToolDefinition.InputSchema.Required, "uid")
    End Sub

    ''' <summary>fill：uid + value 必填。</summary>
    <TestMethod>
    Public Sub Fill_Definition_UidValueRequired()
        Dim tool = MakeTool(Of FillTool)()

        Assert.AreEqual(WpfDebugMethods.Fill, tool.Method)
        CollectionAssert.Contains(tool.ToolDefinition.InputSchema.Required, "uid")
        CollectionAssert.Contains(tool.ToolDefinition.InputSchema.Required, "value")
    End Sub

    ''' <summary>evaluate：script 必填，权限是 Ask（区别于其它只读工具的 Allow，因为脚本可跑任意代码）。</summary>
    <TestMethod>
    Public Sub Evaluate_Definition_ScriptRequired_AskPermission()
        Dim tool = MakeTool(Of EvaluateTool)()

        Assert.AreEqual(WpfDebugMethods.Evaluate, tool.Method)
        Assert.AreEqual("evaluate", tool.ToolDefinition.Name)
        Assert.AreEqual(PermissionLevel.Ask, tool.DefaultPermission)
        CollectionAssert.Contains(tool.ToolDefinition.InputSchema.Required, "script")
    End Sub

    ''' <summary>take_screenshot：两可选参数，无 required。</summary>
    <TestMethod>
    Public Sub TakeScreenshot_Definition_TwoOptionalProps_NoRequired()
        Dim tool = MakeTool(Of TakeScreenshotTool)()

        Assert.AreEqual(WpfDebugMethods.TakeScreenshot, tool.Method)
        Assert.AreEqual("take_screenshot", tool.ToolDefinition.Name)
        Assert.AreEqual(PermissionLevel.Allow, tool.DefaultPermission)
        Assert.IsNull(tool.ToolDefinition.InputSchema.Required)
        Dim props = tool.ToolDefinition.InputSchema.Properties
        Assert.IsTrue(props.ContainsKey("windowId"))
        Assert.IsTrue(props.ContainsKey("uid"))
    End Sub

#End Region

#Region "参数构造 BuildParams（反射访问 protected 方法）"

    ''' <summary>list_windows 无参：BuildParams 返回 Nothing。</summary>
    <TestMethod>
    Public Sub ListWindows_BuildParams_NoArguments_ReturnsNothing()
        Dim tool = MakeTool(Of ListWindowsTool)()

        Dim paramsObj = InvokeBuildParams(tool, New Dictionary(Of String, Object)())

        Assert.IsNull(paramsObj)
    End Sub

    ''' <summary>take_snapshot 全可选参数缺省：windowId/maxDepth 为 Nothing，interestingOnly 为 False。</summary>
    <TestMethod>
    Public Sub TakeSnapshot_BuildParams_AllMissing_DefaultsApplied()
        Dim tool = MakeTool(Of TakeSnapshotTool)()

        Dim jo As JObject = JObject.FromObject(
            InvokeBuildParams(tool, New Dictionary(Of String, Object)()))

        Assert.AreEqual(JTokenType.Null, jo("windowId").Type)
        Assert.AreEqual(JTokenType.Null, jo("maxDepth").Type)
        Assert.IsFalse(jo("interestingOnly").Value(Of Boolean)())
    End Sub

    ''' <summary>take_snapshot 显式参数：全部透传。</summary>
    <TestMethod>
    Public Sub TakeSnapshot_BuildParams_AllProvided_AllPassedThrough()
        Dim tool = MakeTool(Of TakeSnapshotTool)()
        Dim args As New Dictionary(Of String, Object) From {
            {"windowId", "win1"},
            {"maxDepth", 5},
            {"interestingOnly", True}
        }

        Dim jo As JObject = JObject.FromObject(InvokeBuildParams(tool, args))

        Assert.AreEqual("win1", jo("windowId").Value(Of String)())
        Assert.AreEqual(5, jo("maxDepth").Value(Of Integer)())
        Assert.IsTrue(jo("interestingOnly").Value(Of Boolean)())
    End Sub

    ''' <summary>take_snapshot 空 windowId 字符串应归一成 Nothing（被控端契约：空=不指定）。</summary>
    <TestMethod>
    Public Sub TakeSnapshot_BuildParams_EmptyWindowId_NormalizedToNothing()
        Dim tool = MakeTool(Of TakeSnapshotTool)()
        Dim args As New Dictionary(Of String, Object) From {{"windowId", ""}}

        Dim jo As JObject = JObject.FromObject(InvokeBuildParams(tool, args))

        Assert.AreEqual(JTokenType.Null, jo("windowId").Type)
    End Sub

    ''' <summary>take_snapshot 非法 maxDepth 不应抛异常，按未指定处理（容错，不硬崩）。</summary>
    <TestMethod>
    Public Sub TakeSnapshot_BuildParams_InvalidMaxDepth_NormalizedToNothing()
        Dim tool = MakeTool(Of TakeSnapshotTool)()
        Dim args As New Dictionary(Of String, Object) From {{"maxDepth", "abc"}}

        Dim jo As JObject = JObject.FromObject(InvokeBuildParams(tool, args))

        Assert.AreEqual(JTokenType.Null, jo("maxDepth").Type)
    End Sub

    ''' <summary>click 缺 uid：抛 McpException（必填校验，沿用基类 ValidateRequiredArguments 风格）。</summary>
    <TestMethod>
    Public Sub Click_BuildParams_MissingUid_Throws()
        Dim tool = MakeTool(Of ClickTool)()

        Try
            InvokeBuildParams(tool, New Dictionary(Of String, Object)())
            Assert.Fail("缺 uid 不应通过校验")
        Catch ex As TargetInvocationException
            Assert.IsInstanceOfType(ex.InnerException, GetType(McpException))
        End Try
    End Sub

    ''' <summary>click 有 uid：透传。</summary>
    <TestMethod>
    Public Sub Click_BuildParams_WithUid_PassedThrough()
        Dim tool = MakeTool(Of ClickTool)()
        Dim args As New Dictionary(Of String, Object) From {{"uid", "u1"}}

        Dim jo As JObject = JObject.FromObject(InvokeBuildParams(tool, args))

        Assert.AreEqual("u1", jo("uid").Value(Of String)())
    End Sub

    ''' <summary>fill 缺 value：抛 McpException。</summary>
    <TestMethod>
    Public Sub Fill_BuildParams_MissingValue_Throws()
        Dim tool = MakeTool(Of FillTool)()
        Dim args As New Dictionary(Of String, Object) From {{"uid", "u1"}}

        Try
            InvokeBuildParams(tool, args)
            Assert.Fail("缺 value 不应通过校验")
        Catch ex As TargetInvocationException
            Assert.IsInstanceOfType(ex.InnerException, GetType(McpException))
        End Try
    End Sub

    ''' <summary>evaluate 缺 script：抛 McpException。</summary>
    <TestMethod>
    Public Sub Evaluate_BuildParams_MissingScript_Throws()
        Dim tool = MakeTool(Of EvaluateTool)()

        Try
            InvokeBuildParams(tool, New Dictionary(Of String, Object)())
            Assert.Fail("缺 script 不应通过校验")
        Catch ex As TargetInvocationException
            Assert.IsInstanceOfType(ex.InnerException, GetType(McpException))
        End Try
    End Sub

    ''' <summary>evaluate script + timeoutMs：透传，timeoutMs 为整型。</summary>
    <TestMethod>
    Public Sub Evaluate_BuildParams_WithTimeout_PassedThrough()
        Dim tool = MakeTool(Of EvaluateTool)()
        Dim args As New Dictionary(Of String, Object) From {
            {"script", "Return 1+1"},
            {"timeoutMs", 5000}
        }

        Dim jo As JObject = JObject.FromObject(InvokeBuildParams(tool, args))

        Assert.AreEqual("Return 1+1", jo("script").Value(Of String)())
        Assert.AreEqual(5000, jo("timeoutMs").Value(Of Integer)())
    End Sub

    ''' <summary>evaluate 不带 timeout：timeoutMs 为 Nothing（契约：不限）。</summary>
    <TestMethod>
    Public Sub Evaluate_BuildParams_NoTimeout_NormalizedToNothing()
        Dim tool = MakeTool(Of EvaluateTool)()
        Dim args As New Dictionary(Of String, Object) From {{"script", "x"}}

        Dim jo As JObject = JObject.FromObject(InvokeBuildParams(tool, args))

        Assert.AreEqual(JTokenType.Null, jo("timeoutMs").Type)
    End Sub

    ''' <summary>take_screenshot 两参数都缺：均为 Nothing。</summary>
    <TestMethod>
    Public Sub TakeScreenshot_BuildParams_AllMissing_BothNothing()
        Dim tool = MakeTool(Of TakeScreenshotTool)()

        Dim jo As JObject = JObject.FromObject(
            InvokeBuildParams(tool, New Dictionary(Of String, Object)()))

        Assert.AreEqual(JTokenType.Null, jo("windowId").Type)
        Assert.AreEqual(JTokenType.Null, jo("uid").Type)
    End Sub

#End Region

#Region "响应格式化 FormatResult（反射访问 protected 方法）"

    ''' <summary>list_windows 业务返回值是窗口数组：透传，不文本化。</summary>
    <TestMethod>
    Public Sub ListWindows_FormatResult_ArrayPayload_PassedThrough()
        Dim tool = MakeTool(Of ListWindowsTool)()
        Dim arr As New JArray()
        arr.Add(New JObject From {{"windowId", "w1"}})
        arr.Add(New JObject From {{"windowId", "w2"}})

        Dim result As JToken = CType(InvokeFormatResult(tool, arr), JToken)

        Assert.AreEqual(JTokenType.Array, result.Type)
        Assert.HasCount(2, result)
    End Sub

    ''' <summary>list_windows 无返回值（被控端没窗口时的兜底）：返回空数组。</summary>
    <TestMethod>
    Public Sub ListWindows_FormatResult_NothingPayload_ReturnsEmptyArray()
        Dim tool = MakeTool(Of ListWindowsTool)()

        Dim result As JToken = CType(InvokeFormatResult(tool, Nothing), JToken)

        Assert.AreEqual(JTokenType.Array, result.Type)
        Assert.IsEmpty(result)
    End Sub

    ''' <summary>click（Sub 方法）无 payload：返回成功确认。</summary>
    <TestMethod>
    Public Sub Click_FormatResult_NothingPayload_ReturnsSuccess()
        Dim tool = MakeTool(Of ClickTool)()

        Dim result As JObject = JObject.FromObject(InvokeFormatResult(tool, Nothing))

        Assert.IsTrue(result("success").Value(Of Boolean)())
    End Sub

    ''' <summary>evaluate 标量字符串 payload：提取成 result 字段。</summary>
    <TestMethod>
    Public Sub Evaluate_FormatResult_StringPayload_ExtractedToResult()
        Dim tool = MakeTool(Of EvaluateTool)()

        Dim result As JObject = JObject.FromObject(
            InvokeFormatResult(tool, New JValue("42")))

        Assert.AreEqual("42", result("result").Value(Of String)())
    End Sub

    ''' <summary>evaluate 无 payload（脚本 Return Nothing）：result 为 Nothing。</summary>
    <TestMethod>
    Public Sub Evaluate_FormatResult_NothingPayload_ResultNothing()
        Dim tool = MakeTool(Of EvaluateTool)()

        Dim result As JObject = JObject.FromObject(InvokeFormatResult(tool, Nothing))

        Assert.AreEqual(JTokenType.Null, result("result").Type)
    End Sub

    ''' <summary>take_screenshot payload 透传（ScreenshotResult 结构）。</summary>
    <TestMethod>
    Public Sub TakeScreenshot_FormatResult_PayloadPassedThrough()
        Dim tool = MakeTool(Of TakeScreenshotTool)()
        Dim payload As New JObject From {
            {"width", 800},
            {"height", 600},
            {"pngBase64", "iVBOR"}
        }

        Dim result As JObject = CType(InvokeFormatResult(tool, payload), JObject)

        Assert.AreEqual(800, result("width").Value(Of Integer)())
        Assert.AreEqual("iVBOR", result("pngBase64").Value(Of String)())
    End Sub

    ''' <summary>take_screenshot 无 payload（异常情况）：返回失败提示。</summary>
    <TestMethod>
    Public Sub TakeScreenshot_FormatResult_NothingPayload_ReturnsFailure()
        Dim tool = MakeTool(Of TakeScreenshotTool)()

        Dim result As JObject = JObject.FromObject(InvokeFormatResult(tool, Nothing))

        Assert.IsFalse(result("success").Value(Of Boolean)())
    End Sub

#End Region

#Region "快照文本渲染 SnapshotFormatter（公开静态方法，纯函数）"

    ''' <summary>单节点：含 uid/类型/名称/属性，渲染成一行。</summary>
    <TestMethod>
    Public Sub SnapshotFormatter_SingleNode_FormatsOneLine()
        Dim node As New JObject From {
            {"uid", "100"},
            {"typeName", "Button"},
            {"name", "OK"}
        }
        Dim props As New JObject From {{"IsEnabled", True}}
        node("properties") = props

        Dim text As String = SnapshotFormatter.Format(node)

        StringAssert.Contains(text, "uid=100")
        StringAssert.Contains(text, "Button")
        StringAssert.Contains(text, """OK""")
        StringAssert.Contains(text, "IsEnabled=True")
    End Sub

    ''' <summary>嵌套子节点：子节点缩进 2 空格。</summary>
    <TestMethod>
    Public Sub SnapshotFormatter_NestedChildren_ChildIndented()
        Dim child As New JObject From {
            {"uid", "2"},
            {"typeName", "TextBlock"}
        }
        Dim children As New JArray()
        children.Add(child)
        Dim root As New JObject From {
            {"uid", "1"},
            {"typeName", "Grid"},
            {"children", children}
        }

        Dim text As String = SnapshotFormatter.Format(root)

        Dim lines() As String = text.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
        Assert.HasCount(2, lines)
        StringAssert.StartsWith(lines(0), "uid=1")
        StringAssert.StartsWith(lines(1), "  uid=2")
    End Sub

    ''' <summary>三层嵌套：缩进每层 +2 空格。</summary>
    <TestMethod>
    Public Sub SnapshotFormatter_ThreeLevelDepth_IndentationGrows()
        Dim grandChild As New JObject From {
            {"uid", "3"},
            {"typeName", "Run"}
        }
        Dim grandChildren As New JArray()
        grandChildren.Add(grandChild)
        Dim child As New JObject From {
            {"uid", "2"},
            {"typeName", "TextBlock"},
            {"children", grandChildren}
        }
        Dim children As New JArray()
        children.Add(child)
        Dim root As New JObject From {
            {"uid", "1"},
            {"typeName", "Grid"},
            {"children", children}
        }

        Dim text As String = SnapshotFormatter.Format(root)

        Dim lines() As String = text.Split(New String() {vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries)
        Assert.HasCount(3, lines)
        StringAssert.StartsWith(lines(0), "uid=1")
        StringAssert.StartsWith(lines(1), "  uid=2")
        StringAssert.StartsWith(lines(2), "    uid=3")
    End Sub

    ''' <summary>无 name/properties 的节点也能渲染，不崩。</summary>
    <TestMethod>
    Public Sub SnapshotFormatter_MinimalNode_DoesNotCrash()
        Dim node As New JObject From {
            {"uid", "1"},
            {"typeName", "Border"}
        }

        Dim text As String = SnapshotFormatter.Format(node)

        StringAssert.Contains(text, "uid=1")
        StringAssert.Contains(text, "Border")
    End Sub

    ''' <summary>Nothing 节点返回空字符串。</summary>
    <TestMethod>
    Public Sub SnapshotFormatter_NothingNode_ReturnsEmpty()
        Assert.AreEqual("", SnapshotFormatter.Format(Nothing))
    End Sub

#End Region

#Region "辅助"

    ''' <summary>用 NullLogger / AlwaysAllow 权限处理器造工具实例（无副作用）。
    ''' 不用 New 约束：六个工具的构造函数都是 (logger, permissionHandler) 两参，没有无参构造。
    ''' </summary>
    Private Function MakeTool(Of T As WpfDebugToolBase)() As T
        Dim logger As New NullLogger()
        Dim permission As New AlwaysAllowPermissionHandler()
        Return DirectCast(Activator.CreateInstance(GetType(T), logger, permission), T)
    End Function

    ''' <summary>反射调用 protected BuildParams。TargetInvocationException 包装真实异常。</summary>
    Private Function InvokeBuildParams(tool As WpfDebugToolBase, args As Dictionary(Of String, Object)) As Object
        Dim method As MethodInfo = tool.GetType().GetMethod(
            "BuildParams",
            BindingFlags.Instance Or BindingFlags.NonPublic Or BindingFlags.Public)
        Return method.Invoke(tool, New Object() {args})
    End Function

    ''' <summary>反射调用 protected FormatResult。</summary>
    Private Function InvokeFormatResult(tool As WpfDebugToolBase, payload As JToken) As Object
        Dim method As MethodInfo = tool.GetType().GetMethod(
            "FormatResult",
            BindingFlags.Instance Or BindingFlags.NonPublic Or BindingFlags.Public)
        Return method.Invoke(tool, New Object() {payload})
    End Function

#End Region
End Class

''' <summary>
''' 空日志记录器：吃掉所有日志，测试无副作用（不写文件/不输出）。
''' </summary>
Public Class NullLogger
    Implements IMcpLogger

    Public Sub LogMcpRequest(operation As String, result As String, details As String) Implements IMcpLogger.LogMcpRequest
        ' 测试不打日志。
    End Sub

    Public Sub LogServiceAction(action As String, result As String, details As String) Implements IMcpLogger.LogServiceAction
        ' 测试不打日志。
    End Sub
End Class

''' <summary>
''' 始终允许的权限处理器：测试时跳过权限弹框（无 UI 交互副作用）。
''' </summary>
Public Class AlwaysAllowPermissionHandler
    Implements IMcpPermissionHandler

    Public Function CheckPermission(featureName As String, description As String) As Boolean Implements IMcpPermissionHandler.CheckPermission
        Return True
    End Function

    Public Function CheckFilePermission(featureName As String, description As String, filePath As String, accessType As FileAccessType) As Boolean Implements IMcpPermissionHandler.CheckFilePermission
        Return True
    End Function
End Class
