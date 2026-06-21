Imports System.Collections
Imports System.Globalization
Imports System.Text
Imports Newtonsoft.Json

' 脚本执行结果的序列化纯函数。
' evaluate 契约返回 String（IWpfDebugTarget.Evaluate），把任意脚本返回值（对象/null/标量/集合）
' 序列化成可读字符串（Newtonsoft.Json 序列化 + ToString 兜底）。
'
' 这层特意抽成无副作用纯函数，便于单测覆盖各种类型/循环引用/异常对象分支（#21 单测要求）。
' 不碰 WPF 对象、不碰 UI 线程、不启 Roslyn、不加装配载——零副作用。

''' <summary>
''' 脚本返回值的序列化器（纯函数集合）。
''' </summary>
Public NotInheritable Class ScriptResultSerializer

    Private Sub New()
    End Sub

    ' 默认最大序列化深度（防循环引用炸栈）。超过深度截断为 "[maxDepth]"。
    Private Const DefaultMaxDepth As Integer = 20

    ''' <summary>
    ''' 把任意脚本返回值序列化成可读字符串。null → "null"；标量/集合走 JSON；兜底 ToString。
    ''' </summary>
    Public Shared Function Serialize(value As Object) As String
        Return Serialize(value, DefaultMaxDepth)
    End Function

    ''' <summary>
    ''' 带最大深度的序列化（单测可调深度）。纯函数。
    ''' </summary>
    Public Shared Function Serialize(value As Object, maxDepth As Integer) As String
        If value Is Nothing Then Return "null"
        If maxDepth <= 0 Then Return "[maxDepth]"

        ' 1) 字符串：包引号但保留转义（JSON 风格）。
        Dim s As String = TryCast(value, String)
        If s IsNot Nothing Then Return JsonConvert.ToString(s)

        ' 2) 基础标量（数字/布尔/DateTime/枚举）：直接用 InvariantCulture ToString。
        Dim primitive As String = TrySerializePrimitive(value)
        If primitive IsNot Nothing Then Return primitive

        ' 3) 集合（IEnumerable）走 JSON 数组（含深度限制）。
        Dim enumerable As IEnumerable = TryCast(value, IEnumerable)
        If enumerable IsNot Nothing AndAlso Not (TypeOf value Is IDictionary) Then
            Return SerializeEnumerable(enumerable, maxDepth)
        End If

        ' 4) 字典走 JSON 对象。
        Dim dict As IDictionary = TryCast(value, IDictionary)
        If dict IsNot Nothing Then
            Return SerializeDictionary(dict, maxDepth)
        End If

        ' 5) 复杂对象：用 JSON 序列化（Newtonsoft 已能处理循环引用），失败退化 ToString。
        Try
            ' ReferenceLoopHandling.Ignore：循环引用不抛，截断为 null。
            ' MaxDepth 限制深度避免栈爆。
            Return JsonConvert.SerializeObject(value, New JsonSerializerSettings With {
                .ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                .MaxDepth = maxDepth,
                .Formatting = Formatting.Indented,
                .Culture = CultureInfo.InvariantCulture
            })
        Catch
            Return SafeToString(value)
        End Try
    End Function

    ' 把基础标量（数字/布尔/DateTime/枚举/char）序列化成 JSON 字面量或 InvariantCulture 字符串。
    ' 不命中返回 Nothing（调用方继续走其它分支）。
    Private Shared Function TrySerializePrimitive(value As Object) As String
        Dim t As Type = value.GetType()

        ' 枚举：取名字（字符串形式，便于 AI 读懂）。
        If t.IsEnum Then Return JsonConvert.SerializeObject(value.ToString())

        ' Boolean：JSON true/false。
        If TypeOf value Is Boolean Then
            Return If(CBool(value), "true", "false")
        End If

        ' 整数族：原样输出。
        If TypeOf value Is SByte OrElse TypeOf value Is Byte OrElse
           TypeOf value Is Short OrElse TypeOf value Is UShort OrElse
           TypeOf value Is Integer OrElse TypeOf value Is UInteger OrElse
           TypeOf value Is Long OrElse TypeOf value Is ULong Then
            Return Convert.ToString(value, CultureInfo.InvariantCulture)
        End If

        ' 浮点族：用 InvariantCulture（点号小数点），NaN/Infinity 用 JSON 兜底。
        If TypeOf value Is Single OrElse TypeOf value Is Double OrElse TypeOf value Is Decimal Then
            Dim d As Double = Convert.ToDouble(value, CultureInfo.InvariantCulture)
            If Double.IsNaN(d) OrElse Double.IsInfinity(d) Then
                Return $"""{Convert.ToString(value, CultureInfo.InvariantCulture)}"""
            End If
            Return Convert.ToString(value, CultureInfo.InvariantCulture)
        End If

        ' DateTime/DateTimeOffset/TimeSpan：ISO 风格字符串。
        If TypeOf value Is DateTime Then
            Return $"""{DirectCast(value, DateTime).ToString("o", CultureInfo.InvariantCulture)}"""
        End If
        If TypeOf value Is DateTimeOffset Then
            Return $"""{DirectCast(value, DateTimeOffset).ToString("o", CultureInfo.InvariantCulture)}"""
        End If
        If TypeOf value Is TimeSpan Then
            Return $"""{DirectCast(value, TimeSpan).ToString()}"""
        End If

        ' Char：包引号。
        If TypeOf value Is Char Then
            Return JsonConvert.SerializeObject(CChar(value).ToString())
        End If

        ' Guid：包引号。
        If TypeOf value Is Guid Then
            Return $"""{DirectCast(value, Guid).ToString()}"""
        End If

        Return Nothing
    End Function

    ' IEnumerable → JSON 数组，深度限制。
    Private Shared Function SerializeEnumerable(enumerable As IEnumerable, maxDepth As Integer) As String
        Dim sb As New StringBuilder()
        sb.Append("["c)
        Dim first As Boolean = True
        Dim count As Integer = 0
        Const MaxItems As Integer = 500 ' 防超大集合（如百万节点树）撑爆序列化。
        For Each item As Object In enumerable
            If count >= MaxItems Then
                sb.Append(", ...(truncated)")
                Exit For
            End If
            If Not first Then sb.Append(", ")
            sb.Append(Serialize(item, maxDepth - 1))
            first = False
            count += 1
        Next
        sb.Append("]"c)
        Return sb.ToString()
    End Function

    ' IDictionary → JSON 对象，深度限制。
    Private Shared Function SerializeDictionary(dict As IDictionary, maxDepth As Integer) As String
        Dim sb As New StringBuilder()
        sb.Append("{"c)
        Dim first As Boolean = True
        For Each entry As DictionaryEntry In dict
            If Not first Then sb.Append(", ")
            sb.Append(JsonConvert.SerializeObject(Convert.ToString(entry.Key, CultureInfo.InvariantCulture)))
            sb.Append(": ")
            sb.Append(Serialize(entry.Value, maxDepth - 1))
            first = False
        Next
        sb.Append("}"c)
        Return sb.ToString()
    End Function

    ' 兜底：ToString 不抛（异常对象上 ToString 可能炸）。
    Private Shared Function SafeToString(value As Object) As String
        Try
            Return value.ToString()
        Catch
            Return $"[{value.GetType().Name}]"
        End Try
    End Function
End Class
