# VBScriptDotNet 的编译执行与程序集加载

本文讲 VBScriptDotNet（VBScript.NET，扩展名 `.vbx`）这套脚本是怎么把一段 VB 风格的源码编译成 .NET 代码并跑起来的，重点放在程序集引用（assembly reference）这条线上：编译时引用从哪里来、运行时这些引用对应的 dll 又是怎么被加载和解析的，以及在 .NET Framework 和 .NET 10（新 .NET）这两个差别很大的运行时上分别用了什么机制。所有引用的源码都标了 Windows 绝对路径，方便直接跳过去核对。

## 先看清楚这套东西的来路

VBScriptDotNet 本质上是把 Roslyn 的脚本引擎（`Microsoft.CodeAnalysis.Scripting`）搬过来、配上一个 VB 专属的壳。解决方案 `G:\Projects\VBScriptDotNet\VBInteractive.sln` 里真正承担编译执行的就两组项目：

- 脚本引擎库：`G:\Projects\VBScriptDotNet\Scripting\Core\Microsoft.CodeAnalysis.Scripting.csproj`（语言无关的核心）和 `G:\Projects\VBScriptDotNet\Scripting\VisualBasic\Microsoft.CodeAnalysis.VisualBasic.Scripting.vbproj`（VB 专属部分）。这两个库都多目标 `netstandard2.0;net10.0`，也就是说它们本身得在 .NET Framework 和新 .NET 上都能跑。
- 宿主可执行：`G:\Projects\VBScriptDotNet\Interactive\vbi\vbi.vbproj`，多目标 `net10.0-windows;net10.0;net48`，产物就是 `vbi.exe`，是真正吃 `.vbx` 文件的入口。

另外 `G:\Projects\VBScriptDotNet\Installer\` 下面有几个打包用的宿主：`vbifw.vbproj`（单目标 `net48`，纯 .NET Framework 版的 `vbi.exe`）、`vbicore.vbproj`（`net10.0-windows`，带 WinUI 的现代桌面版）、`vbichooser.vbproj`（MSIX 包入口，负责根据脚本声明选择启动哪个 `vbi.exe`）。这几个宿主都复用同一份 `Interactive\vbi\` 下的 VB 源码，靠 csproj 里的条件编译和不同的 `.rsp` 响应文件区分行为。

引擎库对 Roslyn 的依赖走的是 NuGet：`Microsoft.CodeAnalysis.Common` 5.3.0 和 `Microsoft.CodeAnalysis.VisualBasic` 5.3.0（见两个项目文件里的 `PackageReference`）。也就是说编译用的就是 Roslyn 这套现成的编译器 API，没有自己重写 VB 语法分析或 IL 生成。引擎库还通过 `InternalsVisibleTo` 和 `IgnoresAccessChecksToGenerator` 访问 Roslyn 的 internal 成员，所以它和 Roslyn 主仓库里的脚本实现是一脉相承的。

## 编译这条主线：脚本源码怎么变成 .NET 代码

入口在 `G:\Projects\VBScriptDotNet\Interactive\vbi\Vbi.vb` 的 `Vbi.Main`。它做的事很少：设一下控制台标题、初始化 WinForms 的可视样式（这部分用 `#If WINDOWS7_0_OR_GREATER` / `#If NETFRAMEWORK` 条件编译区分两边），然后调用 `OnStartup`，后者算出 `vbi.exe` 所在目录，调 `VisualBasicScript.RunInteractive(args, vbiDirectory, "vbi.rsp")`。

`RunInteractive` 在 `G:\Projects\VBScriptDotNet\Scripting\VisualBasic\VisualBasicScript.vb` 里。它把 `vbiDirectory` 拼上响应文件名 `vbi.rsp`，构造一个 `VisualBasicInteractiveCompiler`（定义在 `G:\Projects\VBScriptDotNet\Scripting\VisualBasic\Hosting\CommandLine\Vbi.vb`），再交给 `CommandLineRunner` 跑。注意这个 `vbi.rsp` 是构建时从 `vbi.desktop.rsp`（net48）或 `vbi.coreclr.rsp`（net10）拷过来的——具体由 `vbi.vbproj` 里 `Condition="'$(TargetFramework)' == 'net48'"` 决定走哪个。两份响应文件的差异后面讲引用时再说。

`CommandLineRunner`（`G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs`）是真正把命令行、响应文件、脚本文件串起来的地方。它的 `RunInteractiveCore` 干这几件事：用 Roslyn 的命令行解析器（`VisualBasicCommandLineParser.Script`）解析参数和 `.vbx` 路径；读出脚本源码；调 `GetScriptOptions` 把命令行里的 `/r:` 引用、`/imports:` 命名空间等组装成一个 `ScriptOptions`；最后根据是交互模式还是单文件模式，走 `RunInteractiveLoop`（REPL）或 `RunScript`（一次跑完）。

`GetScriptOptions` 里有个关键调用：`arguments.ResolveMetadataReferences(metadataResolver, ...)`。这一步会把命令行和响应文件里所有 `/r:` 指定的引用，通过 `RuntimeMetadataReferenceResolver` 解析成实际的 `MetadataReference`（指向磁盘上的 dll）。解析失败的会在这一步报诊断错误。

真正的编译发生在 `Script.Compile` / `Script.RunAsync` 这条链上（`G:\Projects\VBScriptDotNet\Scripting\Core\Script.cs`）。`Script<T>.GetExecutor` 懒构造一个执行委托，交给 `ScriptBuilder.CreateExecutor<T>`。`ScriptBuilder`（`G:\Projects\VBScriptDotNet\Scripting\Core\ScriptBuilder.cs`）的 `Build` 方法是核心：

1. 调 `compilation.GetEntryPoint` 拿到入口点符号。
2. 用 `Compilation.Emit` 把脚本编译成内存里的 PE 流和可选的 PDB 流。这一步是纯 Roslyn 的能力——VB 脚本被当成一个 `Submission#N` 类型的 script compilation，输出 `DynamicallyLinkedLibrary`。
3. 编译成功后，遍历 `compilation.GetBoundReferenceManager().GetReferencedAssemblies()`，对每个带文件路径的引用，调 `_assemblyLoader.RegisterDependency(identity, path)`，把“这个程序集标识对应这个磁盘路径”告诉加载器。这一步是为运行时依赖解析铺路，非常关键，后面展开。
4. 调 `_assemblyLoader.LoadAssemblyFromStream(peStream, pdbStream)` 把刚生成的程序集加载进当前进程。
5. 反射取出入口点方法，做成 `Func<object[], Task<T>>` 委托返回。

VB 那侧怎么构造 compilation，看 `G:\Projects\VBScriptDotNet\Scripting\VisualBasic\VisualBasicScriptCompiler.vb` 的 `CreateSubmission`。它调 `VisualBasicCompilation.CreateScriptCompilation`，传进去的引用清单来自 `script.GetReferencesForCompilation(...)`，外加一个固定的 `s_vbRuntimeReference`（`Microsoft.VisualBasic.dll`，通过 `MetadataReference.CreateFromAssemblyInternal(GetType(Strings).Assembly)` 拿到）。编译选项里 `optionStrict:=Off`、`optionInfer:=True`、`embedVbCoreRuntime:=False`，还 `WithIgnoreCorLibraryDuplicatedTypes(True)`——这些就是 VBScript 风格“宽松类型推断、不内嵌 VB 核心”的来源。

`Script.GetReferencesForCompilation`（在 `Script.cs`）揭示了一份脚本最终拿到的引用清单是怎么拼出来的。链首脚本（`Previous == null`）会自动加上：CorLib（`typeof(object)` 所在程序集）、GlobalsType 所在程序集（带 `<host>` 别名，用来藏全局类型的命名空间）、语言运行时引用（VB 那侧是 Microsoft.VisualBasic）。然后再加上 `ScriptOptions.MetadataReferences` 里的所有引用——这里头可能是已经 resolved 的 `MetadataReference`，也可能是 `UnresolvedMetadataReference`（字符串形式，如 `"System.Windows.Forms"`），后者会在这一刻通过 `Options.MetadataResolver.ResolveReference` 现场解析。

总结一下编译链路：`.vbx` 源码 → Roslyn `VisualBasicCompilation.CreateScriptCompilation` → `Emit` 到内存流 → `InteractiveAssemblyLoader.LoadAssemblyFromStream` 加载 → 反射拿入口点做委托。整条链没有用 `System.Reflection.Emit` 手写 IL，全是 Roslyn 干的活。

## 引用从哪来：编译期的引用解析

脚本能用什么类型，取决于编译时塞给 Roslyn 的引用清单。VBScriptDotNet 里引用有三个来源，对应不同入口：

第一，默认引用，来自响应文件 `.rsp`。两份响应文件的差异本身就反映了两边运行时布局的不同，值得对照看：

| 引用 | `vbi.desktop.rsp`（net48） | `vbi.coreclr.rsp`（net10） |
|---|---|---|
| `System.Drawing` | 有 | 没有 |
| `System.Windows.Forms` | 有 | 没有 |
| `Microsoft.VisualBasic.Core` | 没有 | 有 |
| `System.Text.Encoding.CodePages` | 没有 | 有 |

`vbi.desktop.rsp` 显式列出 `System.Drawing` 和 `System.Windows.Forms`，因为在 .NET Framework 上它们是独立的 Framework dll，得显式 `/r:`；而 net10 上这些通过 `vbi.vbproj` 里的 `<UseWindowsForms>true</UseWindowsForms>` 和 `<UseWPF>true</UseWPF>` 以 FrameworkReference 形式隐式带入，不用在 rsp 里重复。反过来 net10 多了 `Microsoft.VisualBasic.Core`（新 .NET 把 VB 运行时拆成了 `Microsoft.VisualBasic` 和 `Microsoft.VisualBasic.Core` 两个），以及 CodePages 编码支持。

第二，命令行 `/r:` 引用，用户在调 `vbi.exe` 时手动加。`CommandLineRunner.GetMetadataReferenceResolver` 把命令行的 `ReferencePaths` 和 `BaseDirectory` 一起传给 `RuntimeMetadataReferenceResolver.CreateCurrentPlatformResolver`。

第三，脚本内的 `#R` 指令。`.vbx` 文件顶部可以写 `#R "PresentationCore.dll"` 这样的指令引入额外 dll，比如 `G:\Projects\VBScriptDotNet\Samples\WpfCpuCoreInformation.vbx` 就用 `#R` 引入了三个 WPF 程序集，`G:\Projects\VBScriptDotNet\Samples\WinUISimpleWindow.vbx` 引入了 `Microsoft.WinUI`。这些 `#R` 指令由 Roslyn 的脚本解析器吃进去，最终也走 `ScriptOptions.MetadataResolver.ResolveReference` 这条路，和命令行 `/r:` 殊途同归。

真正干解析活的 `RuntimeMetadataReferenceResolver` 在 `G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs`。它的 `ResolveReference` 按这个顺序找：

1. 先看是不是 NuGet 包引用（`nuget:包名,版本` 格式），是就走 `NuGetPackageResolver`。
2. 再看是不是文件路径形态，是的话先查 `TrustedPlatformAssemblies`（CoreCLR 那侧的平台程序集表，下面细讲），找不到再用 `RelativePathResolver` 在搜索路径和基准目录里找。
3. 否则当成程序集显示名：先查 GAC（`GacFileResolver.Resolve`），再查 `TrustedPlatformAssemblies`。

还有个 `ResolveMissingAssembly`，处理 compilation 绑定时发现的“间接依赖”——某个引用 dll 自己又引用了别的 dll，但脚本没显式列出。它的查找顺序是：GAC（仅强名程序集）→ TrustedPlatformAssemblies → 引用 dll 所在目录。

这里有个关键变量：`TrustedPlatformAssemblies`。它是 `RuntimeMetadataReferenceResolver.GetTrustedPlatformAssemblyPaths` 从 `AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")` 读出来的，把那个用路径分隔符分隔的字符串解析成“简单名 → 全路径”的字典。这个环境变量只在 CoreCLR（.NET Core / .NET 5+）上有，.NET Framework 上为空——这是两运行时在引用解析上的第一个分水岭。

## 加载这条主线：编译产物怎么进进程

编译生成的 submission 程序集要加载进进程，加上运行时它引用的那些 dll 也要能被找到。这一摊活全归 `InteractiveAssemblyLoader`，在 `G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\AssemblyLoader\InteractiveAssemblyLoader.cs`。

`InteractiveAssemblyLoader` 是对外公开的统一门面，它自己不直接碰运行时的加载 API，而是通过一个内部实现 `_runtimeAssemblyLoader` 干活。这个内部实现是哪个，在构造时由 `AssemblyLoaderImpl.Create(this)` 决定（`G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\AssemblyLoader\AssemblyLoaderImpl.cs`）：

```csharp
if (CoreClrShim.AssemblyLoadContext.Type != null)
    return CreateCoreImpl(loader);   // CoreCLR：走 AssemblyLoadContext
else
    return new DesktopAssemblyLoaderImpl(loader);  // 桌面 FX：走 AppDomain
```

这里的 `CoreClrShim.AssemblyLoadContext.Type` 是在 `G:\Projects\VBScriptDotNet\Compilers\Shared\CoreClrShim.cs` 里通过反射拿的——尝试加载 `System.Runtime.Loader.AssemblyLoadContext` 类型，拿得到说明当前是 CoreCLR（含 .NET 10），拿不到就是 .NET Framework。这是整个项目区分两个运行时的根本开关，而且不是编译期 `#if`，是运行时反射探测——因为引擎库是 netstandard2.0，没法在编译期知道自己最终跑在哪个运行时上。

`InteractiveAssemblyLoader` 还维护几张表，把“路径 → 程序集”“简单名 → 程序集”“简单名 → 已知依赖位置”这些映射管起来，配合 `RegisterDependency` / `ResolveAssembly` 两套接口做依赖解析。前面 `ScriptBuilder.Build` 里调的 `RegisterDependency(identity, path)` 就是把编译期已知的引用路径登记进 `_dependenciesWithLocationBySimpleName`；运行时 CLR 找不到某个依赖、回调到 loader 时，`ResolveAssembly` 就会去这些表里查，按“已加载优先 → 同名同版本 → 同目录文件 → 已登记依赖”的优先级返回。

## 两个运行时上的加载机制对比

这是文档的重点。同一份 `InteractiveAssemblyLoader` 源码，在两个运行时上跑出来的行为差别很大，全靠 `AssemblyLoaderImpl` 的两个子类分流。

### .NET Framework 那侧：AppDomain + AssemblyResolve + GAC

走 `DesktopAssemblyLoaderImpl`（`G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\AssemblyLoader\DesktopAssemblyLoaderImpl.cs`）。它的特点：

- 构造时通过 `CoreLightup.Desktop.AddAssemblyResolveHandler(loader.ResolveAssembly)` 往当前 `AppDomain` 挂一个 `AssemblyResolve` 事件处理器。注意这里用的是反射（`G:\Projects\VBScriptDotNet\Scripting\Core\CoreLightup.cs` 的 `CoreLightup.Desktop`），因为引擎库是 netstandard2.0，编译期看不到 `AppDomain.AssemblyResolve`，运行时再反射拿 `System.AppDomain` 的 `add_AssemblyResolve` 方法挂上去。
- 加载内存里的 PE 流（生成的 submission 程序集）用 `Assembly.Load(byte[], byte[])`。
- 加载磁盘上的 dll 用 `Assembly.LoadFile(path)`。注释里点明了行为：在 GAC 里的程序集会被 `LoadFile` 重定向到 GAC 加载（进 CLR 的 Load Context），不在 GAC 的走 No Context。加载完用反射调 `Assembly.GlobalAssemblyCache` 属性判断是不是来自 GAC，把结果记进 `AssemblyAndLocation`。
- GAC 查询由 `GacFileResolver` 配合 `GlobalAssemblyCache` 完成。`GacFileResolver.IsAvailable` 在 `G:\Projects\VBScriptDotNet\Compilers\Shared\GlobalAssemblyCacheHelpers\GacFileResolver.cs` 里用 `#if !NETCOREAPP` 包了一段 `typeof(object).Assembly.GlobalAssemblyCache`——这是项目里**唯一**一处真正用条件编译区分框架的地方。`GlobalAssemblyCache.Instance`（`G:\Projects\VBScriptDotNet\Compilers\Shared\GlobalAssemblyCacheHelpers\GlobalAssemblyCache.cs`）根据 `Type.GetType("Mono.Runtime")` 选 `MonoGlobalAssemblyCache` 或 `ClrGlobalAssemblyCache`，后者（`ClrGlobalAssemblyCache.cs`）直接 P/Invoke 调 `clr.dll` 的 fusion COM 接口（`CreateAssemblyEnum` / `CreateAssemblyCache` / `IAssemblyName`）枚举和查询 GAC。

`.NET Framework` 上还配了一个影子拷贝机制——`MetadataShadowCopyProvider`（`G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\AssemblyLoader\MetadataShadowCopyProvider.cs`）。它的作用是把引用的 dll 复制到临时目录再加载，避免锁住原文件（REPL 场景下用户可能正在编辑这些 dll，或 dll 被 IDE 锁住）。`InteractiveAssemblyLoader.Load` 里如果传了 shadow copy provider，就先 `GetMetadataShadowCopy` 拿到副本路径再加载；如果发现这个 dll 其实来自 GAC，就调 `SuppressShadowCopy` 跳过拷贝（GAC 里的程序集不需要也不能拷）。

### .NET 10（CoreCLR）那侧：AssemblyLoadContext + Resolving

走 `CoreAssemblyLoaderImpl`（`G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\AssemblyLoader\CoreAssemblyLoaderImpl.cs`）。机制完全不同：

- 内部定义了一个私有嵌套类 `LoadContext : AssemblyLoadContext`。每个 `CoreAssemblyLoaderImpl` 实例都有一个 `_inMemoryAssemblyContext`（基准目录为 null），专门用来加载内存流里的 submission 程序集。
- 加载内存 PE 流用 `_inMemoryAssemblyContext.LoadFromStream(peStream, pdbStream)`。
- 加载磁盘上的 dll 不像 Framework 那样用 `LoadFile`，而是**为每一次加载都新建一个 `LoadContext`**，把基准目录设成 dll 所在目录，然后调 `LoadFromAssemblyPath(path)`。注释说得很直白：本来可以一个目录复用一个 context，但没必要。这种“每个程序集一个独立 ALC”的做法天然给脚本依赖做了隔离，不同脚本引用同一程序集的不同版本也不会互相打架。
- `LoadContext` 重写的 `Load(AssemblyName)` 直接返回 null，并挂了 `Resolving` 事件回调 `_loader.ResolveAssembly(...)`。这是有意为之。`CoreAssemblyLoaderImpl` 的注释解释了 CoreCLR 的解析顺序：先调 `AssemblyLoadContext.Load`（这里返回 null）→ 查 TPA 列表（Trusted Platform Assemblies，即 `TRUSTED_PLATFORM_ASSEMBLIES` 里的那些框架 dll）→ 触发默认上下文的 `Default.Resolving` → 最后才触发当前上下文的 `Resolving`（也就是 loader 自己的回调）。这样设计的目的是让默认上下文先把自己知道的程序集（已加载的、应用 probing 路径里的、平台 dll、宿主应用自己挂的 `Default.Resolving`）解析掉，只有这些都不行时才轮到脚本 loader 介入，避免重复加载。
- 没有 GAC。`GacFileResolver.IsAvailable` 在 net10.0 上因为定义了 `NETCOREAPP` 常量，那段 `#if !NETCOREAPP` 的代码直接被编译器裁掉，剩下 `PlatformInformation.IsRunningOnMono`——在 Windows .NET 10 上也是 false，所以 `GacFileResolver` 根本不会被实例化（`CreateCurrentPlatformResolver` 里 `GacFileResolver.IsAvailable ? new GacFileResolver(...) : null`）。
- 平台程序集的来源是 `TRUSTED_PLATFORM_ASSEMBLIES` 环境变量，`RuntimeMetadataReferenceResolver.GetTrustedPlatformAssemblies` 把它解析成字典。引用解析时如果用户写 `/r:System.Runtime` 这种不带路径的简单名，先在这个字典里找。

### 对照速查

下面这张表把两边的关键差异列在一起，方便查：

| 维度 | .NET Framework（net48） | .NET 10（CoreCLR） |
|---|---|---|
| 加载策略选择 | `AssemblyLoaderImpl.Create` 运行时反射探测 ALC 类型 | 同左（同一套选择逻辑） |
| 实现类 | `DesktopAssemblyLoaderImpl` | `CoreAssemblyLoaderImpl` |
| 内存流加载 | `Assembly.Load(byte[], byte[])` | `AssemblyLoadContext.LoadFromStream` |
| 磁盘加载 | `Assembly.LoadFile`，GAC 内的重定向到 GAC | 每次新建 ALC + `LoadFromAssemblyPath` |
| 依赖解析回调 | `AppDomain.AssemblyResolve`（反射挂） | `AssemblyLoadContext.Resolving` |
| GAC | 有，fusion COM + `GacFileResolver` | 无，`IsAvailable` 编译期就被裁掉 |
| 平台程序集来源 | mscorlib 等进 GAC，靠 GAC 找 | `TRUSTED_PLATFORM_ASSEMBLIES` 环境变量 |
| 影子拷贝 | `MetadataShadowCopyProvider` 支持 | 接口还在，但默认链路不启用 |
| 是否天然隔离 | 同一 AppDomain，靠 loader 自己的表去重 | 每个 dll 独立 ALC，天然隔离 |

## 上下文里的一个细节：TargetFramework 声明

值得单独提一下的是 `.vbx` 顶部的 `'Attribute TargetFramework = "net48"` 这种注释式声明。它不是脚本引擎认识的语法，而是给上层的 `vbichooser`（`G:\Projects\VBScriptDotNet\Installer\vbichooser\Program.vb`）用的。MSIX 安装后，双击 `.vbx` 会先启动 `vbichooser`，它用正则匹配脚本里的 `'Attribute TargetFramework = "..."`：值以 `net4` 开头就启动 `vbifw\vbi.exe`（net48 宿主），否则启动 `vbichooser\vbi.exe`（net10 宿主）。这层调度发生在脚本引擎之外，但解释了为什么 `ExcelWithNetFramework.vbx`（要用 COM 互操作调 Excel）顶部要写那行——它要确保在 .NET Framework 上跑，那边的 COM 互操作和 GAC 行为更贴合传统 VBScript 习惯。

## 几个值得注意的地方

第一，项目对运行时的区分是**双轨制**的。脚本引擎库（`Microsoft.CodeAnalysis.Scripting`）那侧因为要同时支持 netstandard2.0 和 net10.0，绝大多数地方不敢用 `#if`，而是走运行时反射探测（`CoreClrShim`、`CoreLightup.Desktop`），靠 `AssemblyLoaderImpl.Create` 这种工厂在运行时挑实现。唯一一处真用条件编译的是 `GacFileResolver.IsAvailable` 里的 `#if !NETCOREAPP`，因为 `Assembly.GlobalAssemblyCache` 这个属性在 netstandard2.0 下根本不存在，不裁掉编译不过。宿主层（`vbi.vbproj`、`Vbi.vb`）则相反，因为多目标明确，可以放心用 `#If NETFRAMEWORK` / `#If WINDOWS7_0_OR_GREATER` / `#If USE_WINUI` 做条件编译，连用哪个 `.rsp` 都是 csproj 里按 `$(TargetFramework)` 选的。

第二，`InteractiveAssemblyLoader` 这套抽象其实是从 Roslyn 主仓库的脚本实现继承来的，VBScriptDotNet 没有改它的核心逻辑。项目真正自己写的主要是宿主层（`Vbi.vb`、`vbichooser`、`vbicore` 的 WinUI 适配）和响应文件。所以想理解程序集加载，直接看 `InteractiveAssemblyLoader` + `AssemblyLoaderImpl` 两个子类就够，不用在项目里到处翻。

第三，`ScriptBuilder.Build` 里那个 `RegisterDependency` 循环是连接“编译期”和“运行时”的桥。编译期 Roslyn 知道脚本引用了哪些 dll、每个 dll 的标识和路径；但这些信息运行时的 CLR 不知道（尤其是 No Context 加载的那些）。`RegisterDependency` 把编译期拿到的 `(identity, path)` 对登记进 loader，等 CLR 触发 `AssemblyResolve` / `Resolving` 时，loader 就能据此把间接依赖也找出来。这个设计让脚本能引用一个 dll、然后自动用上那个 dll 自己的依赖，而不必在 rsp 或 `#R` 里把整条依赖链都列出来。

## 相关源码（绝对路径）

要核对细节，按这个清单去看：

- `G:\Projects\VBScriptDotNet\VBInteractive.sln` — 解决方案，项目划分和多目标一目了然。
- `G:\Projects\VBScriptDotNet\Interactive\vbi\vbi.vbproj` — 宿主项目，多目标 `net10.0-windows;net10.0;net48`，按 TargetFramework 选 `.rsp`。
- `G:\Projects\VBScriptDotNet\Interactive\vbi\Vbi.vb` — `Vbi.Main`，宿主入口，用 `#If NETFRAMEWORK` / `#If USE_WINUI` 区分行为。
- `G:\Projects\VBScriptDotNet\Interactive\vbi\vbi.desktop.rsp` / `vbi.coreclr.rsp` — 两份默认引用清单，差异反映两运行时布局。
- `G:\Projects\VBScriptDotNet\Scripting\Core\Script.cs` — `Script` / `Script<T>`，`RunAsync` / `Compile` / `GetReferencesForCompilation`。
- `G:\Projects\VBScriptDotNet\Scripting\Core\ScriptBuilder.cs` — `Build` 方法：Emit 到内存流 + `RegisterDependency` 循环 + `LoadAssemblyFromStream`。
- `G:\Projects\VBScriptDotNet\Scripting\Core\ScriptOptions.cs` — `GetDefaultMetadataReferences`，GAC 平台 vs CoreCLR 平台默认引用的差异。
- `G:\Projects\VBScriptDotNet\Scripting\Core\ScriptMetadataResolver.cs` — 公开的引用解析器，包一层 `RuntimeMetadataReferenceResolver`。
- `G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\Resolvers\RuntimeMetadataReferenceResolver.cs` — 引用解析的核心：`ResolveReference` / `ResolveMissingAssembly` / `TRUSTED_PLATFORM_ASSEMBLIES`。
- `G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\AssemblyLoader\InteractiveAssemblyLoader.cs` — 加载门面 + 依赖解析表管理（`RegisterDependency` / `ResolveAssembly`）。
- `G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\AssemblyLoader\AssemblyLoaderImpl.cs` — 工厂 `Create`，按 `CoreClrShim.AssemblyLoadContext.Type` 选实现。
- `G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\AssemblyLoader\DesktopAssemblyLoaderImpl.cs` — .NET Framework 侧：`Assembly.LoadFile` + `AppDomain.AssemblyResolve`。
- `G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\AssemblyLoader\CoreAssemblyLoaderImpl.cs` — CoreCLR 侧：每 dll 一个 `AssemblyLoadContext` + `Resolving` 事件。
- `G:\Projects\VBScriptDotNet\Scripting\Core\CoreLightup.cs` — `CoreLightup.Desktop`，用反射挂 `AppDomain.AssemblyResolve`、读 `Assembly.GlobalAssemblyCache`。
- `G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\AssemblyLoader\MetadataShadowCopyProvider.cs` — 影子拷贝（.NET Framework 侧特色）。
- `G:\Projects\VBScriptDotNet\Compilers\Shared\CoreClrShim.cs` — 运行时探测：反射找 `System.Runtime.Loader.AssemblyLoadContext`。
- `G:\Projects\VBScriptDotNet\Compilers\Shared\GlobalAssemblyCacheHelpers\GacFileResolver.cs` — `IsAvailable`，项目里唯一一处真用 `#if !NETCOREAPP` 条件编译的地方。
- `G:\Projects\VBScriptDotNet\Compilers\Shared\GlobalAssemblyCacheHelpers\GlobalAssemblyCache.cs` — 抽象基类，按 Mono/CLR 选实现。
- `G:\Projects\VBScriptDotNet\Compilers\Shared\GlobalAssemblyCacheHelpers\ClrGlobalAssemblyCache.cs` — CLR 实现，P/Invoke `clr.dll` 的 fusion COM 接口查 GAC。
- `G:\Projects\VBScriptDotNet\Scripting\VisualBasic\VisualBasicScriptCompiler.vb` — `CreateSubmission`，`VisualBasicCompilation.CreateScriptCompilation` 的 VB 配置。
- `G:\Projects\VBScriptDotNet\Scripting\VisualBasic\VisualBasicScript.vb` — `RunInteractive`，宿主调用入口。
- `G:\Projects\VBScriptDotNet\Scripting\VisualBasic\Hosting\CommandLine\Vbi.vb` — `VisualBasicInteractiveCompiler`，命令行编译器壳。
- `G:\Projects\VBScriptDotNet\Scripting\Core\Hosting\CommandLine\CommandLineRunner.cs` — 命令行 → `ScriptOptions`，`#r` / `/r:` 在这里汇合。
- `G:\Projects\VBScriptDotNet\Installer\vbichooser\Program.vb` — MSIX 入口，正则解析 `'Attribute TargetFramework` 决定启动哪个宿主。
- `G:\Projects\VBScriptDotNet\Samples\WpfCpuCoreInformation.vbx` — 用 `#R` 引入 WPF dll 的样例。
- `G:\Projects\VBScriptDotNet\Samples\ExcelWithNetFramework.vbx` — 顶部 `'Attribute TargetFramework = "net48"` 声明的样例。
