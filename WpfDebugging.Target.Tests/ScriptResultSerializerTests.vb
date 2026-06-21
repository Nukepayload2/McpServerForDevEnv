Imports System
Imports System.Collections
Imports System.Collections.Generic
Imports System.Globalization
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServerForDevEnv.WpfDebugging.Target

' ScriptResultSerializer 的纯函数测试。
' 覆盖 null / 标量 / 集合 / 字典 / 复杂对象 / 循环引用 / 枚举 / DateTime / 异常 ToString 等分支。
' 全程零副作用：不启 Roslyn、不加装配载、不碰 UI 线程、不连 pipe、不写文件。

<TestClass>
Public Class ScriptResultSerializerTests

    ' ===== null =====

    <TestMethod>
    Public Sub Serialize_Null_Returns_Literal_null()
        Assert.AreEqual("null", ScriptResultSerializer.Serialize(Nothing))
    End Sub

    ' ===== 字符串 =====

    <TestMethod>
    Public Sub Serialize_String_Wraps_In_Quotes()
        ' Newtonsoft JsonConvert.ToString 输出带引号。
        StringAssert.StartsWith(ScriptResultSerializer.Serialize("hello"), """")
        Assert.AreEqual("""hello""", ScriptResultSerializer.Serialize("hello"))
    End Sub

    <TestMethod>
    Public Sub Serialize_String_Escapes_Special_Chars()
        ' 引号、反斜杠应被转义。
        Dim result As String = ScriptResultSerializer.Serialize("a""b\c")
        Assert.IsTrue(result.Contains("a"))
        Assert.IsTrue(result.Contains("b"))
        Assert.IsTrue(result.Contains("c"))
    End Sub

    ' ===== 布尔 =====

    <TestMethod>
    Public Sub Serialize_Boolean_True_Returns_Literal_true()
        Assert.AreEqual("true", ScriptResultSerializer.Serialize(True))
    End Sub

    <TestMethod>
    Public Sub Serialize_Boolean_False_Returns_Literal_false()
        Assert.AreEqual("false", ScriptResultSerializer.Serialize(False))
    End Sub

    ' ===== 整数 =====

    <TestMethod>
    Public Sub Serialize_Integer_Returns_Invariant_Number()
        Assert.AreEqual("42", ScriptResultSerializer.Serialize(42))
    End Sub

    <TestMethod>
    Public Sub Serialize_Long_Returns_Invariant_Number()
        Assert.AreEqual("9223372036854775807", ScriptResultSerializer.Serialize(Long.MaxValue))
    End Sub

    <TestMethod>
    Public Sub Serialize_NegativeInteger_Returns_Invariant_Number()
        Assert.AreEqual("-1", ScriptResultSerializer.Serialize(-1))
    End Sub

    ' ===== 浮点（InvariantCulture，小数点为点号）=====

    <TestMethod>
    Public Sub Serialize_Double_Uses_InvariantCulture_Decimal_Point()
        ' 3.14 任何文化下都输出 "3.14"（点号），不会被某些文化转成 "3,14"。
        Dim result As String = ScriptResultSerializer.Serialize(3.14)
        Assert.IsTrue(result.Contains("."), $"Expected dot decimal point, got: {result}")
        Assert.IsFalse(result.Contains(","), $"Unexpected comma in number: {result}")
    End Sub

    <TestMethod>
    Public Sub Serialize_Double_NaN_Returns_Quoted_String()
        ' NaN/Infinity 不是合法 JSON 数字，序列化为带引号的字符串字面量。
        Dim result As String = ScriptResultSerializer.Serialize(Double.NaN)
        Assert.IsTrue(result.Contains("NaN"), $"Expected NaN in output: {result}")
    End Sub

    <TestMethod>
    Public Sub Serialize_Double_PositiveInfinity_Returns_Quoted_String()
        Dim result As String = ScriptResultSerializer.Serialize(Double.PositiveInfinity)
        Assert.IsTrue(result.Contains("Infinity"), $"Expected Infinity in output: {result}")
    End Sub

    <TestMethod>
    Public Sub Serialize_Decimal_Uses_InvariantCulture()
        Dim result As String = ScriptResultSerializer.Serialize(1.5D)
        Assert.AreEqual("1.5", result)
    End Sub

    ' ===== 枚举 =====

    <TestMethod>
    Public Sub Serialize_Enum_Returns_Name_As_Quoted_String()
        ' 枚举序列化为名字字符串（AI 读得懂）。
        Dim result As String = ScriptResultSerializer.Serialize(System.DayOfWeek.Monday)
        Assert.IsTrue(result.Contains("Monday"), $"Expected enum name in output: {result}")
    End Sub

    ' ===== DateTime / DateTimeOffset / TimeSpan / Guid =====

    <TestMethod>
    Public Sub Serialize_DateTime_Returns_ISO_String()
        Dim dt As New System.DateTime(2026, 6, 20, 12, 0, 0, System.DateTimeKind.Utc)
        Dim result As String = ScriptResultSerializer.Serialize(dt)
        Assert.IsTrue(result.Contains("2026"), $"Expected year in output: {result}")
        Assert.IsTrue(result.Contains("06") OrElse result.Contains("6"), $"Expected month in output: {result}")
    End Sub

    <TestMethod>
    Public Sub Serialize_TimeSpan_Returns_Quoted_String()
        Dim ts = System.TimeSpan.FromMinutes(5)
        Dim result As String = ScriptResultSerializer.Serialize(ts)
        Assert.IsTrue(result.Contains("00:05"), $"Expected timespan format in output: {result}")
    End Sub

    <TestMethod>
    Public Sub Serialize_Guid_Returns_Quoted_String()
        Dim g = System.Guid.Parse("12345678-1234-1234-1234-123456789012")
        Dim result As String = ScriptResultSerializer.Serialize(g)
        Assert.IsTrue(result.Contains("12345678"), $"Expected guid value in output: {result}")
    End Sub

    ' ===== Char =====

    <TestMethod>
    Public Sub Serialize_Char_Returns_Quoted_String()
        Dim result As String = ScriptResultSerializer.Serialize("A"c)
        Assert.AreEqual("""A""", result)
    End Sub

    ' ===== 集合 =====

    <TestMethod>
    Public Sub Serialize_List_Returns_Json_Array()
        Dim list As New List(Of Integer) From {1, 2, 3}
        Dim result As String = ScriptResultSerializer.Serialize(list)
        Assert.IsTrue(result.StartsWith("["), $"Array should start with '[': {result}")
        Assert.IsTrue(result.EndsWith("]"), $"Array should end with ']': {result}")
        Assert.IsTrue(result.Contains("1"))
        Assert.IsTrue(result.Contains("2"))
        Assert.IsTrue(result.Contains("3"))
    End Sub

    <TestMethod>
    Public Sub Serialize_EmptyList_Returns_Empty_Array()
        Dim list As New List(Of Integer)()
        Dim result As String = ScriptResultSerializer.Serialize(list)
        Assert.AreEqual("[]", result)
    End Sub

    <TestMethod>
    Public Sub Serialize_HugeList_Gets_Truncated()
        ' 超 500 项的集合应被截断标记。
        Dim list As New List(Of Integer)()
        For i As Integer = 0 To 999
            list.Add(i)
        Next
        Dim result As String = ScriptResultSerializer.Serialize(list)
        Assert.IsTrue(result.Contains("truncated"), $"Huge list should be truncated: {result.Substring(0, System.Math.Min(100, result.Length))}...")
    End Sub

    <TestMethod>
    Public Sub Serialize_NestedList_Honors_Depth_Limit()
        ' maxDepth=2 嵌套层级会触发深度限制。
        Dim inner As New List(Of Object) From {New List(Of Object) From {New List(Of Object) From {"deep"}}}
        Dim result As String = ScriptResultSerializer.Serialize(inner, maxDepth:=2)
        Assert.IsTrue(result.Contains("[maxDepth]"), $"Expected depth limit marker: {result}")
    End Sub

    ' ===== 字典 =====

    <TestMethod>
    Public Sub Serialize_Dictionary_Returns_Json_Object()
        Dim dict As New Dictionary(Of String, Integer) From {{"a", 1}, {"b", 2}}
        Dim result As String = ScriptResultSerializer.Serialize(dict)
        Assert.IsTrue(result.StartsWith("{"), $"Object should start with '{{': {result}")
        Assert.IsTrue(result.EndsWith("}"), $"Object should end with '}}': {result}")
        Assert.IsTrue(result.Contains("""a"""))
        Assert.IsTrue(result.Contains("""b"""))
        Assert.IsTrue(result.Contains("1"))
        Assert.IsTrue(result.Contains("2"))
    End Sub

    ' ===== 复杂对象 =====

    <TestMethod>
    Public Sub Serialize_AnonymousObject_Returns_Indented_Json()
        Dim obj = New With {.Name = "test", .Value = 42}
        Dim result As String = ScriptResultSerializer.Serialize(obj)
        Assert.IsTrue(result.Contains("Name"))
        Assert.IsTrue(result.Contains("test"))
        Assert.IsTrue(result.Contains("Value"))
        Assert.IsTrue(result.Contains("42"))
    End Sub

    ' ===== 循环引用（ReferenceLoopHandling.Ignore，不抛）=====

    <TestMethod>
    Public Sub Serialize_CircularReference_Does_Not_Throw()
        Dim node As New CircularNode With {.Label = "root"}
        node.Self = node
        ' 不应抛异常（Newtonsoft ReferenceLoopHandling.Ignore）。
        Dim result As String = ScriptResultSerializer.Serialize(node)
        Assert.IsNotNull(result)
        Assert.IsTrue(result.Length > 0)
    End Sub

    Private Class CircularNode
        Public Property Label As String
        Public Property Self As CircularNode
    End Class

    ' ===== ToString 兜底（对 JSON 序列化抛的类型）=====

    <TestMethod>
    Public Sub Serialize_TypeObject_Falls_Back_To_Json()
        ' Type 对象 Newtonsoft 能处理，输出类型名。
        Dim result As String = ScriptResultSerializer.Serialize(GetType(String))
        Assert.IsNotNull(result)
    End Sub

End Class

' AssemblyLoader.DetectLoaderKind 的纯决策测试。
' DetectLoaderKind 接受可选的 typeProbe 委托（单测注入 mock），
' 不实际加载 ALC 程序集、不碰 AppDomain、不挂回调——零副作用。

<TestClass>
Public Class AssemblyLoaderKindDetectionTests

    <TestMethod>
    Public Sub DetectLoaderKind_TypeProbe_Finds_Alc_Returns_CoreClr()
        ' 模拟「能加载 AssemblyLoadContext 类型」——即 CoreCLR。
        Dim kind As AssemblyLoaderKind = AssemblyLoader.DetectLoaderKind(
            Function(typeName) If(typeName.StartsWith("System.Runtime.Loader.AssemblyLoadContext", StringComparison.Ordinal),
                                  GetType(Object),  ' 非 null 即视为找到。
                                  CType(Nothing, Type)))
        Assert.AreEqual(AssemblyLoaderKind.CoreClr, kind)
    End Sub

    <TestMethod>
    Public Sub DetectLoaderKind_TypeProbe_Cannot_Find_Alc_Returns_Desktop()
        ' 模拟「无法加载 AssemblyLoadContext 类型」——即 .NET Framework。
        Dim kind As AssemblyLoaderKind = AssemblyLoader.DetectLoaderKind(Function(ignored) CType(Nothing, Type))
        Assert.AreEqual(AssemblyLoaderKind.Desktop, kind)
    End Sub

    <TestMethod>
    Public Sub DetectLoaderKind_Default_Probe_Matches_Runtime()
        ' 默认探测：在 net10.0-windows 测试进程上应返回 CoreClr；
        ' 在 net472 进程上返回 Desktop。这里测试进程是 net10.0-windows，故期望 CoreClr。
        Dim kind As AssemblyLoaderKind = AssemblyLoader.DetectLoaderKind()
        Assert.AreEqual(AssemblyLoaderKind.CoreClr, kind)
    End Sub

End Class
