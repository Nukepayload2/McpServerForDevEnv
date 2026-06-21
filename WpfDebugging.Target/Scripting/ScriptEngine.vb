Imports System.Collections.Generic
Imports System.IO
Imports System.Reflection
Imports System.Runtime.CompilerServices
Imports System.Text
Imports Microsoft.CodeAnalysis
Imports Microsoft.CodeAnalysis.Emit
Imports Microsoft.CodeAnalysis.VisualBasic
Imports Newtonsoft.Json.Linq

' VB.NET 脚本引擎：编译 → 内存 PE → 加载 → 执行入口点 → 序列化结果。
' 链路直接借鉴 VBScriptDotNet（参考 Research\WpfDebugging\docs\VBScriptDotNet-编译执行与程序集加载.md）：
'   源（用户脚本片段） → 包装成 Module + Function(host) 入口 → VisualBasicCompilation → Emit 到内存 PE
'   → AssemblyLoader.LoadFromStream → 反射拿入口点 → 调用 → 序列化返回值。
'
' 【懒加载】Roslyn 体积大（几十 MB）。Roslyn 相关类型只在本类内部出现；
' ScriptEngine 实例由 WpfDebugTargetImpl 用 Lazy(Of ScriptEngine) 延迟构造，
' 不调 evaluate 的被控端进程不会触发 Roslyn 程序集加载。
' （rough-plan 第十一节 3）
'
' 【线程约定】Execute 在 UI 线程被调用（evaluate 整体已被切到 UI 线程）。
' 编译、加载、执行全部在 UI 线程同步做；脚本碰元素天然安全。
'
' 【超时】软超时：ScriptEngine 启动一个 Stopwatch，把「是否超时」的检查器注入 ScriptHost。
' 脚本调用 host 方法时检查；超时即抛 ScriptTimeoutException 让执行栈自然展开。
' 编译/加载阶段不计入超时（必须完成）。
' 极端死循环（脚本不调 host、纯计算死循环）无法被中断——这是已知 trade-off，
' 由 IPC 层 ReadIdleTimeoutMs 断连兜底（参考 rough-plan 第十一节 4）。
' （如需硬中断需改设计，向 main 决策——见任务文档「超时」红线。）
'
' 【异常约定】所有编译/加载/执行/超时异常由 Execute 内部捕获并序列化成错误结果字符串返回，
' 不裸抛到 pipe 分派层（WpfDebugPipeServer.OkResult 包成 {"result":<JToken>}）。
'
' 注：本包根命名空间是 McpServerForDevEnv.WpfDebugging.Target，WPF 类型一律 Global.System.Windows.* 完整限定。

''' <summary>
''' VB.NET 脚本引擎。封装脚本编译/加载/执行/结果序列化的完整链路。
''' </summary>
Public NotInheritable Class ScriptEngine

    Private ReadOnly _uidRegistry As UidRegistry
    Private ReadOnly _loader As AssemblyLoader
    ' 引用程序集（MetadataReference）缓存：首次执行时从已加载程序集构造一次，后续脚本复用。
    ' 缓存失效条件：宿主加载了新程序集后下一次脚本仍能看到——这里用「首次构造即快照」，
    ' 简单稳妥；若宿主中途加载关键程序集导致脚本编译找不到类型，调用方 RefreshReferences 即可重建。
    Private _references As List(Of MetadataReference)
    Private _referencesLock As New Object()

    Public Sub New(uidRegistry As UidRegistry)
        If uidRegistry Is Nothing Then Throw New ArgumentNullException(NameOf(uidRegistry))
        _uidRegistry = uidRegistry
        _loader = New AssemblyLoader()
    End Sub

    ''' <summary>
    ''' 编译并执行一段 VB.NET 脚本，返回结果字符串（成功/失败/返回值）。
    ''' 脚本片段会被包成 Function(host As ScriptHost) As Object 的函数体，
    ''' 用 <c>Return &lt;expr&gt;</c> 显式返回结果；不写 Return 等价于返回 Nothing。
    ''' </summary>
    ''' <param name="script">用户脚本源（VB 代码片段，不需要 Module/Function 声明）。</param>
    ''' <param name="timeoutMs">执行超时毫秒；Nothing 表示不限。</param>
    ''' <returns>结果字符串，形如 JSON：
    ''' <c>{"success":true,"value":"...","logs":["..."]}</c> 或
    ''' <c>{"success":false,"error":"...","errorType":"..."}</c>。
    ''' 外层 WpfDebugPipeServer.OkResult 会再包一层 {"result":&lt;JToken&gt;}。</returns>
    Public Function Execute(script As String, timeoutMs As Integer?) As String
        If String.IsNullOrEmpty(script) Then
            Return FailureResult("脚本为空。", Nothing, Nothing)
        End If

        Dim logs As New List(Of String)()

        ' 1) 编译（不计入超时）。
        Dim assembly As Assembly = Nothing
        Try
            assembly = CompileToAssembly(script)
        Catch ex As Exception
            Return FailureResult("脚本编译失败：" & ex.Message, ex.GetType().Name, logs)
        End Try

        ' 2) 反射拿入口点。
        Dim entryMethod As MethodInfo = Nothing
        Try
            entryMethod = ResolveEntryMethod(assembly)
        Catch ex As Exception
            Return FailureResult("入口点解析失败：" & ex.Message, ex.GetType().Name, logs)
        End Try

        ' 3) 执行（带软超时）。host 每次新建，传入超时检查器。
        Dim stopwatch As New System.Diagnostics.Stopwatch()
        stopwatch.Start()
        Dim timeoutTicks As Long = If(timeoutMs.HasValue, timeoutMs.Value * System.Diagnostics.Stopwatch.Frequency \ 1000L, Long.MaxValue)

        Dim host As New ScriptHost(_uidRegistry,
                                   Function() stopwatch.ElapsedTicks > timeoutTicks,
                                   Sub(msg) logs.Add(msg))

        Dim returnValue As Object = Nothing
        Try
            returnValue = InvokeEntry(entryMethod, host)
        Catch ex As ScriptTimeoutException
            Return FailureResult("脚本执行超时（" & If(timeoutMs, 0).ToString() & " ms）。",
                                 NameOf(ScriptTimeoutException), logs)
        Catch ex As Exception
            Return FailureResult("脚本执行失败：" & ex.Message, ex.GetType().FullName, logs)
        Finally
            stopwatch.[Stop]()
        End Try

        ' 4) 序列化返回值。
        Dim valueString As String
        Try
            valueString = ScriptResultSerializer.Serialize(returnValue)
        Catch ex As Exception
            Return FailureResult("返回值序列化失败：" & ex.Message, ex.GetType().Name, logs)
        End Try

        Return SuccessResult(valueString, logs)
    End Function

    ' ===== 引用程序集构造 =====

    ''' <summary>
    ''' 强制重建引用程序集列表（首次执行后宿主又加载了关键程序集时调用）。
    ''' </summary>
    Public Sub RefreshReferences()
        SyncLock _referencesLock
            _references = Nothing
        End SyncLock
    End Sub

    ' 从当前已加载程序集构造 MetadataReference 列表。
    ' net472 遍历 AppDomain.CurrentDomain.GetAssemblies；net8.0 遍历 AssemblyLoadContext.Default.Assemblies
    ' （具体由 AssemblyLoader.Impl.GetLoadedAssemblies 抽象，不分 #If）。
    ' 去重 + 过滤动态/无 Location 的内存程序集（噪声）。
    Private Function GetOrCreateReferences() As List(Of MetadataReference)
        SyncLock _referencesLock
            If _references IsNot Nothing Then Return _references

            Dim refs As New List(Of MetadataReference)
            Dim seenNames As New HashSet(Of String)(StringComparer.Ordinal)

            For Each asm As Assembly In _loader.GetLoadedAssembliesForCompile()
                Try
                    Dim simpleName As String = GetSimpleName(asm)
                    If String.IsNullOrEmpty(simpleName) Then Continue For
                    If Not seenNames.Add(simpleName) Then Continue For

                    Dim reference As MetadataReference = TryCreateReference(asm)
                    If reference IsNot Nothing Then refs.Add(reference)
                Catch
                    ' 单个程序集构造失败不影响整体（跳过它，相关类型脚本用不了，但其它类型仍可用）。
                End Try
            Next

            _references = refs
            Return refs
        End SyncLock
    End Function

    ' 取程序集简单名（ GetName().Name 在某些动态程序集上抛，兜底用 FullName 切分）。
    Private Shared Function GetSimpleName(asm As Assembly) As String
        Try
            Return asm.GetName().Name
        Catch
            Dim full As String = asm.FullName
            If String.IsNullOrEmpty(full) Then Return Nothing
            Dim comma As Integer = full.IndexOf(","c)
            Return If(comma >= 0, full.Substring(0, comma), full)
        End Try
    End Function

    ' 把程序集转成 MetadataReference。优先用磁盘 Location（MetadataReference.CreateFromFile，
    ' 内部用内存映射读取 PE，不锁定文件）。动态程序集 / 无 Location 的内存程序集跳过（作引用无意义）。
    Private Shared Function TryCreateReference(asm As Assembly) As MetadataReference
        ' 跳过动态程序集（Emit 出来的，无 Location，作为引用无意义）。
        If asm.IsDynamic Then Return Nothing

        Dim location As String = Nothing
        Try
            location = asm.Location
        Catch
            ' 某些 NetStandard 内存程序集 Location 抛 PlatformNotSupportedException。
        End Try
        If String.IsNullOrEmpty(location) Then Return Nothing

        Try
            Return MetadataReference.CreateFromFile(location)
        Catch
            Return Nothing
        End Try
    End Function

    ' ===== 编译 =====

    ' 把脚本片段包成 Module + Function(host) As Object 入口点，编译，Emit 到内存 PE，加载返回程序集。
    Private Function CompileToAssembly(script As String) As Assembly
        Dim sourceTree As SyntaxTree = VisualBasicSyntaxTree.ParseText(WrapScript(script))

        ' 默认 Imports：常见命名空间，让脚本里写 w.Title 而非 Imports System.Windows。
        Dim importsList As String() = {
            "System", "System.Collections.Generic", "System.Linq", "System.Text",
            "System.Windows", "System.Windows.Controls", "System.Windows.Input",
            "System.Windows.Media", "System.Threading", "System.Reflection",
            "McpServerForDevEnv.WpfDebugging.Target"
        }
        Dim globalImports As New List(Of GlobalImport)()
        For Each ns As String In importsList
            globalImports.Add(GlobalImport.Parse(ns))
        Next

        Dim compilation As VisualBasicCompilation = VisualBasicCompilation.Create(
            "WpfDebugScript_" & Guid.NewGuid().ToString("N"),
            {sourceTree},
            GetOrCreateReferences(),
            New VisualBasicCompilationOptions(
                outputKind:=OutputKind.DynamicallyLinkedLibrary,
                globalImports:=globalImports,
                optionStrict:=OptionStrict.Off,
                optionInfer:=True,
                optionExplicit:=True,
                optionCompareText:=False,
                optimizationLevel:=OptimizationLevel.Debug,
                checkOverflow:=False))

        ' Emit 到内存 PE。PDB 在 .NET Core 上走便携式，.NET Framework 上走完整——
        ' EmitOptions 默认就行，不强制 PDB（脚本不需要符号调试）。
        Using peStream As New MemoryStream()
            Dim emitResult As EmitResult = compilation.Emit(peStream)
            If Not emitResult.Success Then
                Throw New InvalidOperationException(FormatDiagnostics(emitResult.Diagnostics))
            End If
            Return _loader.LoadFromStream(peStream.ToArray())
        End Using
    End Function

    ' 把用户脚本片段包成可编译的 Module + Function。脚本里 Return <expr> 即返回值。
    Private Shared Function WrapScript(script As String) As String
        Dim sb As New StringBuilder()
        sb.AppendLine("Friend Module GeneratedScriptModule")
        sb.AppendLine("    Friend Function Execute(host As ScriptHost) As Object")
        sb.AppendLine(script)
        sb.AppendLine("        Return Nothing")
        sb.AppendLine("    End Function")
        sb.AppendLine("End Module")
        Return sb.ToString()
    End Function

    ' 把诊断信息格式化成多行字符串（错误/警告）。
    Private Shared Function FormatDiagnostics(diagnostics As IEnumerable(Of Diagnostic)) As String
        Dim sb As New StringBuilder()
        Dim count As Integer = 0
        For Each d As Diagnostic In diagnostics
            If d.Severity = DiagnosticSeverity.Error OrElse d.Severity = DiagnosticSeverity.Warning Then
                sb.AppendLine($"[{d.Severity}] {d.Id}: {d.GetMessage()}")
                count += 1
                If count >= 50 Then
                    sb.AppendLine("...(more diagnostics truncated)")
                    Exit For
                End If
            End If
        Next
        If sb.Length = 0 Then Return "未知编译错误。"
        Return sb.ToString()
    End Function

    ' ===== 执行 =====

    ' 从编译产物里找入口点 Function Execute(host As ScriptHost) As Object。
    Private Shared Function ResolveEntryMethod(asm As Assembly) As MethodInfo
        ' 编译产物只有一个 Module（脚本模块）+ 一个 Execute 方法。
        For Each modInstance As Reflection.Module In asm.GetModules()
            For Each t As Type In modInstance.GetTypes()
                ' VB Module 编译成静态类（abstract + sealed），跳过普通 Class。
                If Not (t.IsAbstract AndAlso t.IsSealed) Then Continue For
                Dim mi As MethodInfo = t.GetMethod("Execute",
                    BindingFlags.IgnoreCase Or BindingFlags.Static Or BindingFlags.NonPublic Or BindingFlags.Public)
                If mi IsNot Nothing Then Return mi
            Next
        Next
        Throw New InvalidOperationException("脚本程序集中找不到 Execute 入口点。")
    End Function

    ' 调用入口点。脚本里 Return 的值即是返回值；脚本不 Return 则 Function 末尾隐式 Return Nothing。
    Private Shared Function InvokeEntry(entryMethod As MethodInfo, host As ScriptHost) As Object
        Return entryMethod.Invoke(Nothing, New Object() {host})
    End Function

    ' ===== 结果序列化（JSON） =====

    Private Shared Function SuccessResult(valueString As String, logs As List(Of String)) As String
        Dim jo As New JObject()
        jo("success") = True
        jo("value") = valueString
        If logs IsNot Nothing AndAlso logs.Count > 0 Then
            Dim arr As New JArray()
            For Each l As String In logs
                arr.Add(l)
            Next
            jo("logs") = arr
        End If
        Return jo.ToString()
    End Function

    Private Shared Function FailureResult(message As String, errorType As String, logs As List(Of String)) As String
        Dim jo As New JObject()
        jo("success") = False
        jo("error") = If(message, String.Empty)
        If Not String.IsNullOrEmpty(errorType) Then jo("errorType") = errorType
        If logs IsNot Nothing AndAlso logs.Count > 0 Then
            Dim arr As New JArray()
            For Each l As String In logs
                arr.Add(l)
            Next
            jo("logs") = arr
        End If
        Return jo.ToString()
    End Function
End Class
