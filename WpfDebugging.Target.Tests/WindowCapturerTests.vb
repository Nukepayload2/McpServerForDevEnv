Imports System
Imports System.Text
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServerForDevEnv.WpfDebugging.Target

' WindowCapturer 的纯逻辑测试：
'   - ToBase64：byte[] → base64 字符串。纯内存计算，零副作用。
' 真实截图（CaptureWindow/CaptureElement：RenderTargetBitmap.Render 必须 UI 线程 + 真实布局完成的 Visual）
' 涉及真 UI 线程和布局系统，标注留集成验证 #24。
' 本测试类零副作用：不连 pipe、不写文件、不启进程、不碰真 UI 线程。

<TestClass>
Public Class WindowCapturerTests

    <TestMethod>
    Public Sub ToBase64_EmptyArray_ReturnsEmptyString()
        Assert.AreEqual(String.Empty, WindowCapturer.ToBase64(Array.Empty(Of Byte)()))
    End Sub

    <TestMethod>
    Public Sub ToBase64_Null_ReturnsNull()
        Assert.IsNull(WindowCapturer.ToBase64(Nothing))
    End Sub

    <TestMethod>
    Public Sub ToBase64_KnownBytes_MatchStandardBase64()
        ' 经典测试向量："Man" → "TWFu"，与 Convert.ToBase64String 一致。
        Dim bytes As Byte() = Encoding.ASCII.GetBytes("Man")
        Dim expected As String = Convert.ToBase64String(bytes)
        Assert.AreEqual(expected, WindowCapturer.ToBase64(bytes))
        Assert.AreEqual("TWFu", WindowCapturer.ToBase64(bytes))
    End Sub

    <TestMethod>
    Public Sub ToBase64_Roundtrips_Through_Convert_FromBase64String()
        ' 随便一段字节（不碰 UI），编码后能解码回来。
        Dim bytes As Byte() = {&H89, &H50, &H4E, &H47, &H0D, &H0A, &H1A, &H0A} ' PNG 文件签名占位
        Dim base64 As String = WindowCapturer.ToBase64(bytes)
        Dim back As Byte() = Convert.FromBase64String(base64)
        CollectionAssert.AreEqual(bytes, back)
    End Sub

    <TestMethod>
    Public Sub ToBase64_ProducesCanonicalBase64_AlphabetOnly()
        ' base64 输出字符集应仅包含 A–Z/a–z/0–9/+//（无换行：Base64FormattingOptions.None）。
        Dim bytes As Byte() = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15}
        Dim base64 As String = WindowCapturer.ToBase64(bytes)
        For Each c As Char In base64
            Dim ok As Boolean = (c >= "A"c AndAlso c <= "Z"c) OrElse (c >= "a"c AndAlso c <= "z"c) OrElse
                                (c >= "0"c AndAlso c <= "9"c) OrElse c = "+"c OrElse c = "/"c OrElse c = "="c
            Assert.IsTrue(ok, $"非法 base64 字符：{c}")
        Next
        Assert.IsFalse(base64.Contains(ControlChars.Lf))
        Assert.IsFalse(base64.Contains(" "))
    End Sub

    <TestMethod>
    Public Sub ToBase64_LargeArray_HandlesCleanly()
        ' 模拟一段较大字节（KB 级，纯内存，不碰 UI/文件/网络）。
        Dim bytes(4095) As Byte
        Call (New Random(42)).NextBytes(bytes)
        Dim base64 As String = WindowCapturer.ToBase64(bytes)
        Assert.AreEqual(((4096 + 2) \ 3) * 4, base64.Length) ' base64 长度公式：⌈n/3⌉*4（VB 整除 \ 优先级低于 *，须括号）
    End Sub

    <TestMethod>
    Public Sub ToBase64_SingleByte_PadsCorrectly()
        ' 1 字节 → 4 字符（含 2 个 = 填充）。
        Assert.AreEqual("AQ==", WindowCapturer.ToBase64({CByte(1)}))
        ' 2 字节 → 4 字符（含 1 个 = 填充）。
        Assert.AreEqual("AQI=", WindowCapturer.ToBase64({CByte(1), CByte(2)}))
    End Sub

End Class
