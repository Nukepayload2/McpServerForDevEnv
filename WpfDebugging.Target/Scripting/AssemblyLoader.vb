Imports System.Collections.Generic
Imports System.Reflection

' 脚本编译产物（内存 PE）与磁盘依赖 dll 的加载器。
' 双目标核心差异点（rough-plan 第八节 4、第十一节 2）：
'   - net472：AppDomain + Assembly.Load(byte[]) + AppDomain.AssemblyResolve 做依赖解析。
'   - net8.0-windows：AssemblyLoadContext.LoadFromStream + ALC.Resolving 做依赖解析。
'
' 按运行时探测分流，不用 #If 条件编译（rough-plan 明确要求）。
' 探测方式直接借鉴 VBScriptDotNet 的 CoreClrShim.AssemblyLoadContext.Type 反射查找
' （见 Research\WpfDebugging\docs\VBScriptDotNet-编译执行与程序集加载.md「加载这条主线」）：
' 能拿到 System.Runtime.Loader.AssemblyLoadContext 类型 → CoreCLR 分支；否则 .NET Framework 分支。
'
' 线程约定：本类无 WPF 依赖，可在任意线程调用。实际由 ScriptEngine 在 evaluate 链路里使用。
' 去重 + 过滤动态/无 Location 的内存程序集噪声在 ScriptEngine.BuildReferences 那侧做，
' 本类只负责「拿到（简单名, 路径/字节数组）→ 加载 + 依赖解析」。

''' <summary>
''' 程序集加载门面。按当前运行时（CoreCLR vs .NET Framework）反射探测分流到具体实现。
''' 每个 ScriptEngine 实例持有一个，挂各自的依赖解析回调，互不串扰。
''' </summary>
Public NotInheritable Class AssemblyLoader
    Implements IDisposable

    ' 简单名 → 已加载程序集（去重，避免同一程序集重复加载）。
    Private ReadOnly _loadedByName As New Dictionary(Of String, Assembly)(StringComparer.Ordinal)
    ' 编译期登记的间接依赖：简单名 → 磁盘路径（运行时 CLR 找不到时回查）。
    Private ReadOnly _dependencyPaths As New Dictionary(Of String, String)(StringComparer.Ordinal)
    ' 运行时分流实现。Nothing 表示尚未初始化（懒构造）。
    Private _impl As ILoaderImpl
    Private _disposed As Boolean

    Public Sub New()
        _impl = CreateImplForCurrentRuntime(Me)
    End Sub

    ''' <summary>
    ''' 把内存里的脚本编译产物（PE/PDB 字节）加载进进程并返回该程序集。
    ''' 同一加载器实例上重复加载同一字节流不保证去重（脚本每次编译产物都不同）。
    ''' </summary>
    Public Function LoadFromStream(peBytes As Byte(), Optional pdbBytes As Byte() = Nothing) As Assembly
        If _disposed Then Throw New ObjectDisposedException(NameOf(AssemblyLoader))
        If _impl Is Nothing Then Throw New InvalidOperationException("加载器未初始化。")
        Return _impl.LoadFromStream(peBytes, pdbBytes)
    End Function

    ''' <summary>
    ''' 登记一个编译期已知的程序集依赖（脚本引用链上的间接依赖）。
    ''' 运行时 CLR 触发 AssemblyResolve/Resolving 找不到时，本加载器据此把磁盘上的 dll 找出来。
    ''' 直接借鉴 VBScriptDotNet ScriptBuilder.Build 里的 RegisterDependency 循环。
    ''' </summary>
    Public Sub RegisterDependency(simpleName As String, path As String)
        If String.IsNullOrEmpty(simpleName) OrElse String.IsNullOrEmpty(path) Then Return
        SyncLock _dependencyPaths
            ' 已存在不覆盖（先登记者优先，通常是宿主已加载的版本）。
            If Not _dependencyPaths.ContainsKey(simpleName) Then
                _dependencyPaths(simpleName) = path
            End If
        End SyncLock
    End Sub

    ''' <summary>
    ''' 当前已加载程序集的枚举器（供 ScriptEngine 构造编译引用清单用）。
    ''' net472 走 AppDomain.CurrentDomain.GetAssemblies；net8.0 走 AssemblyLoadContext.Default.Assemblies
    ''' （由 ILoaderImpl.GetLoadedAssemblies 抽象，本类不分 #If）。
    ''' </summary>
    Friend Function GetLoadedAssembliesForCompile() As IEnumerable(Of Assembly)
        If _impl Is Nothing Then Return Array.Empty(Of Assembly)()
        Return _impl.GetLoadedAssemblies()
    End Function

    ''' <summary>
    ''' 运行时依赖解析回调（被 AppDomain.AssemblyResolve / ALC.Resolving 调用）。
    ''' 按「已加载优先 → 已登记依赖路径」优先级查找。找不到返回 Nothing（让 CLR 继续走默认解析）。
    ''' </summary>
    Friend Function ResolveAssembly(simpleName As String) As Assembly
        If String.IsNullOrEmpty(simpleName) Then Return Nothing

        ' 1) 已加载优先：避免重复加载同一程序集。
        Dim loaded As Assembly = Nothing
        SyncLock _loadedByName
            If _loadedByName.TryGetValue(simpleName, loaded) Then Return loaded
        End SyncLock

        ' 2) 在已加载程序集里按简单名兜底找一遍（可能从别的路径加载过同名程序集）。
        Dim found As Assembly = FindLoadedBySimpleName(simpleName)
        If found IsNot Nothing Then
            SyncLock _loadedByName
                _loadedByName(simpleName) = found
            End SyncLock
            Return found
        End If

        ' 3) 已登记依赖路径 → 加载磁盘 dll。
        Dim path As String = Nothing
        SyncLock _dependencyPaths
            _dependencyPaths.TryGetValue(simpleName, path)
        End SyncLock
        If String.IsNullOrEmpty(path) Then Return Nothing

        Try
            Dim asm As Assembly = _impl.LoadFromPath(path)
            SyncLock _loadedByName
                _loadedByName(simpleName) = asm
            End SyncLock
            Return asm
        Catch
            Return Nothing
        End Try
    End Function

    ' 在当前已加载程序集里按简单名找（兜底）。遍历运行时已加载集合，对.net framework 是 AppDomain.GetAssemblies，
    ' 对 coreclr 是 AssemblyLoadContext.Default.Assemblies。这里走 _impl 提供的统一枚举器避免条件编译。
    Private Function FindLoadedBySimpleName(simpleName As String) As Assembly
        If _impl Is Nothing Then Return Nothing
        For Each asm As Assembly In _impl.GetLoadedAssemblies()
            Try
                If GetNameSimpleName(asm).Equals(simpleName, StringComparison.Ordinal) Then Return asm
            Catch
            End Try
        Next
        Return Nothing
    End Function

    ' 取程序集简单名（Assembly.GetName().Name 在某些动态程序集上可能抛，兜底用 FullName 切分）。
    Private Shared Function GetNameSimpleName(asm As Assembly) As String
        Try
            Dim n As AssemblyName = asm.GetName()
            Return If(n.Name, String.Empty)
        Catch
            Dim full As String = asm.FullName
            If String.IsNullOrEmpty(full) Then Return String.Empty
            Dim comma As Integer = full.IndexOf(","c)
            Return If(comma >= 0, full.Substring(0, comma), full)
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If _disposed Then Return
        _disposed = True
        Dim impl As ILoaderImpl = _impl
        _impl = Nothing
        impl?.Detach()
    End Sub

    ' ===== 运行时分流（核心：可单测的纯函数 + 实际构造）=====

    ''' <summary>
    ''' 按运行时反射探测决定走哪个加载实现。纯决策（不构造实例），便于单测。
    ''' 探测方式：能否加载 System.Runtime.Loader.AssemblyLoadContext 类型 —— 能则是 CoreCLR，否则 .NET Framework。
    ''' </summary>
    Public Shared Function DetectLoaderKind(Optional typeProbe As Func(Of String, Type) = Nothing) As AssemblyLoaderKind
        ' 默认用 Type.GetType 探测；单测可注入 mock typeProbe。
        Dim probe As Func(Of String, Type) = If(typeProbe, AddressOf Type.GetType)
        Dim alcType As Type = probe("System.Runtime.Loader.AssemblyLoadContext, System.Runtime.Loader")
        If alcType IsNot Nothing Then Return AssemblyLoaderKind.CoreClr
        Return AssemblyLoaderKind.Desktop
    End Function

    ' 实际构造分流实现（带副作用：挂 AssemblyResolve / Resolving 回调）。
    Private Shared Function CreateImplForCurrentRuntime(owner As AssemblyLoader) As ILoaderImpl
        Select Case DetectLoaderKind()
            Case AssemblyLoaderKind.CoreClr
                Return New CoreClrLoaderImpl(owner)
            Case Else
                Return New DesktopLoaderImpl(owner)
        End Select
    End Function

    ' ===== 分流实现接口 =====

    Friend Interface ILoaderImpl
        Function LoadFromStream(peBytes As Byte(), pdbBytes As Byte()) As Assembly
        Function LoadFromPath(path As String) As Assembly
        Function GetLoadedAssemblies() As IEnumerable(Of Assembly)
        Sub Detach()
    End Interface

    ' .NET Framework / net472：AppDomain + Assembly.Load(byte[]) + AssemblyResolve。
    ' 借鉴 VBScriptDotNet DesktopAssemblyLoaderImpl（参考文档「.NET Framework 那侧」一节）。
    Friend NotInheritable Class DesktopLoaderImpl
        Implements ILoaderImpl

        Private ReadOnly _owner As AssemblyLoader
        Private ReadOnly _resolveHandler As ResolveEventHandler
        Private _appDomain As AppDomain

        Public Sub New(owner As AssemblyLoader)
            _owner = owner
            _appDomain = AppDomain.CurrentDomain
            _resolveHandler = AddressOf HandleAssemblyResolve
            AddHandler _appDomain.AssemblyResolve, _resolveHandler
        End Sub

        ' AssemblyResolve 回调签名：(sender, args) → Assembly。args.Name 是程序集显示名（可含版本/Token）。
        Private Function HandleAssemblyResolve(sender As Object, args As ResolveEventArgs) As Assembly
            Return _owner.ResolveAssembly(SimpleNameOf(args.Name))
        End Function

        ' 从程序集显示名（"Foo, Version=1.0.0.0, ..."）取简单名。
        Private Shared Function SimpleNameOf(displayName As String) As String
            If String.IsNullOrEmpty(displayName) Then Return Nothing
            Dim comma As Integer = displayName.IndexOf(","c)
            Return If(comma >= 0, displayName.Substring(0, comma).Trim(), displayName.Trim())
        End Function

        Public Function LoadFromStreamImpl(peBytes As Byte(), pdbBytes As Byte()) As Assembly _
            Implements ILoaderImpl.LoadFromStream
            ' Assembly.Load(byte[], byte[])：PE + PDB（符号）。PDB 为空时传 Nothing。
            If pdbBytes Is Nothing OrElse pdbBytes.Length = 0 Then
                Return Assembly.Load(peBytes, Nothing)
            End If
            Return Assembly.Load(peBytes, pdbBytes)
        End Function

        Public Function LoadFromPathImpl(path As String) As Assembly _
            Implements ILoaderImpl.LoadFromPath
            ' Assembly.LoadFile：从磁盘路径加载。GAC 内的会被重定向到 GAC。
            Return Assembly.LoadFile(path)
        End Function

        Public Function GetLoadedAssembliesImpl() As IEnumerable(Of Assembly) _
            Implements ILoaderImpl.GetLoadedAssemblies
            Return AppDomain.CurrentDomain.GetAssemblies()
        End Function

        Public Sub DetachImpl() Implements ILoaderImpl.Detach
            If _appDomain IsNot Nothing AndAlso _resolveHandler IsNot Nothing Then
                Try
                    RemoveHandler _appDomain.AssemblyResolve, _resolveHandler
                Catch
                End Try
            End If
            _appDomain = Nothing
        End Sub
    End Class

    ' CoreCLR / net8.0-windows：AssemblyLoadContext + LoadFromStream + Resolving。
    ' 借鉴 VBScriptDotNet CoreAssemblyLoaderImpl（参考文档「.NET 10（CoreCLR）那侧」一节）。
    ' 这里用反射操作 ALC，避免编译期硬引用 System.Runtime.Loader（保持双目标源码统一，不分 #If）。
    Friend NotInheritable Class CoreClrLoaderImpl
        Implements ILoaderImpl

        Private ReadOnly _owner As AssemblyLoader
        ' 反射拿到的 AssemblyLoadContext 类型（System.Runtime.Loader 程序集里）。
        Private ReadOnly _alcType As Type
        ' 专门加载内存流 submission 的 ALC 实例（基准目录 null）。
        Private ReadOnly _inMemoryContext As Object
        ' Resolving 事件卸载委托（挂在 _inMemoryContext 上）。
        Private _resolvingHandler As [Delegate]
        Private _defaultContext As Object ' AssemblyLoadContext.Default，用于 GetLoadedAssemblies 兜底。

        ' LoadFromAssemblyPath 每次新建的临时 ALC 及其 Resolving handler（#21 Major M1 修复）。
        ' 之前每次加载磁盘 dll 新建 ALC 但没挂 Resolving，导致该 ALC 加载的程序集的间接依赖链
        ' 解析失败（新 ALC 的 Resolving 不触发，回不到 owner.ResolveAssembly）。
        ' 这里把每个临时 ALC 也挂上同样的 Resolving，让间接依赖能走 owner.ResolveAssembly 解析。
        Private ReadOnly _pathContexts As New List(Of Object)()
        Private ReadOnly _pathHandlers As New List(Of [Delegate])()

        Public Sub New(owner As AssemblyLoader)
            _owner = owner
            _alcType = Type.GetType("System.Runtime.Loader.AssemblyLoadContext, System.Runtime.Loader", throwOnError:=True)

            ' 构造一个无参基准目录的 ALC 实例（等效 VBScriptDotNet 的 LoadContext(baseDir=null)）。
            ' 用反射调构造函数：AssemblyLoadContext(name, isCollectible)。
            _inMemoryContext = CreateInMemoryContext(_alcType)

            ' 把 Resolving 事件挂到内存 context 上：CLR 解析失败时回调本类。
            _resolvingHandler = AttachResolving(_alcType, _inMemoryContext, AddressOf HandleResolving)

            ' Default 上下文拿一份，用于 GetLoadedAssemblies 兜底（Default.Assemblies 在 .NET 上可用）。
            _defaultContext = _alcType.GetProperty("Default")?.GetValue(Nothing)
        End Sub

        ' 创建内存 ALC 实例。AssemblyLoadContext 是抽象类，构造函数是 protected internal：
        '   protected AssemblyLoadContext(string name, bool isCollectible = false)
        '   protected AssemblyLoadContext()
        ' 反射需带 NonPublic 才能拿到（borrowing VBScriptDotNet LoadContext 基目录 null 思路）。
        Private Shared Function CreateInMemoryContext(alcType As Type) As Object
            Dim allInstance As Reflection.BindingFlags =
                Reflection.BindingFlags.Instance Or Reflection.BindingFlags.Public Or Reflection.BindingFlags.NonPublic

            ' 优先用 ctor(string, bool)（isCollectible=false）。
            Dim ctor2 As ConstructorInfo = alcType.GetConstructor(allInstance, Nothing, {GetType(String), GetType(Boolean)}, Nothing)
            If ctor2 IsNot Nothing Then
                Return ctor2.Invoke(New Object() {"WpfDebugScript", False})
            End If

            ' 退化：ctor(string)。
            Dim ctorString As ConstructorInfo = alcType.GetConstructor(allInstance, Nothing, {GetType(String)}, Nothing)
            If ctorString IsNot Nothing Then
                Return ctorString.Invoke(New Object() {"WpfDebugScript"})
            End If

            ' 退化：无参 ctor。
            Dim ctorDefault As ConstructorInfo = alcType.GetConstructor(allInstance, Nothing, Type.EmptyTypes, Nothing)
            If ctorDefault IsNot Nothing Then
                Return ctorDefault.Invoke(Nothing)
            End If

            Throw New InvalidOperationException("无法构造 AssemblyLoadContext 实例（找不到可用构造函数）。")
        End Function

        ' 反射挂 Resolving 事件。事件签名：AssemblyLoadContext.Resolving(AssemblyLoadContext, AssemblyName) → Assembly。
        Private Shared Function AttachResolving(alcType As Type, context As Object,
                                                handler As Func(Of Object, Object, Assembly)) As [Delegate]
            Dim ei As Reflection.EventInfo = alcType.GetEvent("Resolving")
            If ei Is Nothing Then Return Nothing

            ' 构造一个匹配 Resolving 委托签名的委托。用 反射调委托包装：
            ' 直接用 Func<object,object,Assembly> 转不行（签名不匹配），这里用一个内部适配类。
            Dim adapter As New ResolvingAdapter(handler)
            Dim del As [Delegate] = adapter.BuildDelegate(ei.EventHandlerType, context)
            If del IsNot Nothing Then
                ei.AddEventHandler(context, del)
            End If
            Return del
        End Function

        ' Resolving 回调包装（适配 ALC 类型不明确的情况）。
        Private Function HandleResolving(context As Object, name As Object) As Assembly
            Dim simple As String = TryCast(name, AssemblyName)?.Name
            If String.IsNullOrEmpty(simple) AndAlso name IsNot Nothing Then
                simple = CStr(name.ToString())
            End If
            Return _owner.ResolveAssembly(simple)
        End Function

        Public Function LoadFromStreamImpl(peBytes As Byte(), pdbBytes As Byte()) As Assembly _
            Implements ILoaderImpl.LoadFromStream
            ' 反射调 AssemblyLoadContext.LoadFromStream(Stream, Stream)。
            Using peStream As New IO.MemoryStream(peBytes)
                If pdbBytes Is Nothing OrElse pdbBytes.Length = 0 Then
                    Dim mi As Reflection.MethodInfo = _alcType.GetMethod("LoadFromStream", {GetType(IO.Stream)})
                    Return DirectCast(mi.Invoke(_inMemoryContext, New Object() {peStream}), Assembly)
                End If
                Using pdbStream As New IO.MemoryStream(pdbBytes)
                    Dim mi As Reflection.MethodInfo = _alcType.GetMethod("LoadFromStream", {GetType(IO.Stream), GetType(IO.Stream)})
                    Return DirectCast(mi.Invoke(_inMemoryContext, New Object() {peStream, pdbStream}), Assembly)
                End Using
            End Using
        End Function

        Public Function LoadFromPathImpl(path As String) As Assembly _
            Implements ILoaderImpl.LoadFromPath
            ' CoreCLR 上加载磁盘 dll：新建一个 ALC 实例并 LoadFromAssemblyPath（借鉴 VBScriptDotNet 每 dll 一个 ALC 的做法）。
            ' 【#21 Major M1 修复】新建的 ALC 必须同样挂 Resolving 回调，否则该 ALC 加载的程序集
            ' 一旦有间接依赖链（A → B），解析 B 时新 ALC 的 Resolving 不触发，回不到 owner.ResolveAssembly，
            ' 导致 FileNotFoundException。这里复用 AttachResolving + HandleResolving，与 _inMemoryContext 同款。
            Dim ctx As Object = CreateInMemoryContext(_alcType)
            Dim handler As [Delegate] = AttachResolving(_alcType, ctx, AddressOf HandleResolving)
            SyncLock _pathContexts
                _pathContexts.Add(ctx)
                _pathHandlers.Add(handler)
            End SyncLock
            Dim mi As Reflection.MethodInfo = _alcType.GetMethod("LoadFromAssemblyPath", {GetType(String)})
            Return DirectCast(mi.Invoke(ctx, New Object() {path}), Assembly)
        End Function

        Public Function GetLoadedAssembliesImpl() As IEnumerable(Of Assembly) _
            Implements ILoaderImpl.GetLoadedAssemblies
            ' 优先 Default.Assemblies；拿不到退化到 AppDomain.CurrentDomain.GetAssemblies()（.NET 上仍可用）。
            If _defaultContext IsNot Nothing Then
                Dim asmsProp As Reflection.PropertyInfo = _alcType.GetProperty("Assemblies")
                If asmsProp IsNot Nothing Then
                    Try
                        Return DirectCast(asmsProp.GetValue(_defaultContext), IEnumerable(Of Assembly))
                    Catch
                    End Try
                End If
            End If
            Return AppDomain.CurrentDomain.GetAssemblies()
        End Function

        Public Sub DetachImpl() Implements ILoaderImpl.Detach
            If _inMemoryContext IsNot Nothing AndAlso _resolvingHandler IsNot Nothing Then
                Try
                    Dim ei As Reflection.EventInfo = _alcType.GetEvent("Resolving")
                    ei?.RemoveEventHandler(_inMemoryContext, _resolvingHandler)
                Catch
                End Try
            End If

            ' 清理 LoadFromPath 创建的临时 ALC 上的 Resolving handler（#21 Major M1 修复配套）。
            SyncLock _pathContexts
                Dim ei As Reflection.EventInfo = _alcType.GetEvent("Resolving")
                For i As Integer = 0 To Math.Min(_pathContexts.Count, _pathHandlers.Count) - 1
                    Try
                        ei?.RemoveEventHandler(_pathContexts(i), _pathHandlers(i))
                    Catch
                    End Try
                Next
                _pathContexts.Clear()
                _pathHandlers.Clear()
            End SyncLock
        End Sub

        ' Resolving 委托适配器：用 DynamicMethod 动态生成匹配 EventHandlerType 的委托。
        Private NotInheritable Class ResolvingAdapter
            Private ReadOnly _handler As Func(Of Object, Object, Assembly)

            Public Sub New(handler As Func(Of Object, Object, Assembly))
                _handler = handler
            End Sub

            ' 用一个实例方法包装，反射构造委托到目标事件类型。
            Public Function BuildDelegate(eventHandlerType As Type, context As Object) As [Delegate]
                If eventHandlerType Is Nothing Then Return Nothing
                Try
                    ' 通用做法：Delegate.CreateDelegate(targetType, instance, method)。
                    Dim invokeMethod As Reflection.MethodInfo = eventHandlerType.GetMethod("Invoke")
                    Dim params As Reflection.ParameterInfo() = invokeMethod.GetParameters()
                    ' AssemblyLoadContext.Resolving 签名：(AssemblyLoadContext context, AssemblyName name)。
                    If params.Length = 2 Then
                        Dim tCtx As Type = params(0).ParameterType
                        Dim tName As Type = params(1).ParameterType
                        ' 用 DynamicMethod 生成强类型 thunk，调用回 _handler(this, ctx, name)。
                        Dim dm As New Reflection.Emit.DynamicMethod("ResolvingThunk", GetType(Assembly), {GetType(ResolvingAdapter), tCtx, tName}, GetType(ResolvingAdapter), skipVisibility:=True)
                        Dim il As Reflection.Emit.ILGenerator = dm.GetILGenerator()
                        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0) ' adapter
                        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1) ' ctx
                        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2) ' name
                        il.Emit(System.Reflection.Emit.OpCodes.Callvirt, GetType(ResolvingAdapter).GetMethod("InvokeTyped").MakeGenericMethod(tCtx, tName))
                        il.Emit(System.Reflection.Emit.OpCodes.Ret)
                        Return dm.CreateDelegate(eventHandlerType, Me)
                    End If
                Catch
                End Try
                Return Nothing
            End Function

            ' DynamicMethod 调用的目标：把强类型参数还原成 object 喂给 _handler。
            Public Function InvokeTyped(Of TCtx, TName)(ctx As TCtx, name As TName) As Assembly
                Return _handler(ctx, name)
            End Function
        End Class
    End Class
End Class

''' <summary>
''' 运行时分流类别（AssemblyLoader.DetectLoaderKind 的返回）。
''' </summary>
Public Enum AssemblyLoaderKind
    ''' <summary>.NET Framework（net472）：走 AppDomain + AssemblyResolve。</summary>
    Desktop
    ''' <summary>CoreCLR（net8.0+）：走 AssemblyLoadContext + Resolving。</summary>
    CoreClr
End Enum
