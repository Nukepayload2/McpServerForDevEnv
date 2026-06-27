Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServiceNetFx
Imports McpServerForDevEnv.WpfDebugging
Imports Newtonsoft.Json.Linq

''' <summary>
''' WPF 调试握手帧解析 + WpfDebugConnection 构造的纯逻辑测试。
''' 无副作用：只构造 JObject / 实例化 POCO，不开 pipe、不启进程。
''' </summary>
<TestClass>
Public Class WpfDebugHandshakeTests

    ''' <summary>完整握手帧（三个字段都有）解析正确。</summary>
    <TestMethod>
    Public Sub ParseHandshake_FullFrame_ParsesAllFields()
        Dim frame As New JObject()
        frame("protocolVersion") = "1"
        frame("pid") = 12345
        frame("mainWindowTitle") = "My WPF App - Main"

        Dim info As WpfDebugHandshakeInfo = WpfDebugResultReader.ParseHandshake(frame)

        Assert.IsNotNull(info)
        Assert.AreEqual("1", info.ProtocolVersion)
        Assert.AreEqual(12345, info.Pid)
        Assert.AreEqual("My WPF App - Main", info.MainWindowTitle)
    End Sub

    ''' <summary>被控端握手时主窗口可能还没建好（标题为空）：title 字段缺失应解析为 Nothing。</summary>
    <TestMethod>
    Public Sub ParseHandshake_MissingTitle_ParsesAsNothing()
        Dim frame As New JObject()
        frame("protocolVersion") = "1"
        frame("pid") = 99
        ' 故意不放 mainWindowTitle

        Dim info As WpfDebugHandshakeInfo = WpfDebugResultReader.ParseHandshake(frame)

        Assert.AreEqual("1", info.ProtocolVersion)
        Assert.AreEqual(99, info.Pid)
        Assert.IsNull(info.MainWindowTitle)
    End Sub

    ''' <summary>字段类型容错：pid 用字符串数字也应能取到整型。</summary>
    <TestMethod>
    Public Sub ParseHandshake_PidAsString_ConvertsToInt()
        Dim frame As New JObject()
        frame("protocolVersion") = "1"
        frame("pid") = "7777"

        Dim info As WpfDebugHandshakeInfo = WpfDebugResultReader.ParseHandshake(frame)

        Assert.AreEqual(7777, info.Pid)
    End Sub

    ''' <summary>Nothing 帧应抛 ArgumentNullException。</summary>
    <TestMethod>
    Public Sub ParseHandshake_NullFrame_Throws()
        Try
            WpfDebugResultReader.ParseHandshake(Nothing)
            Assert.Fail("Nothing 帧不应被接受")
        Catch ex As ArgumentNullException
            ' 预期
        End Try
    End Sub

    ''' <summary>空帧（所有字段缺失）应返回字段为默认值/Nothing 的对象，不崩。</summary>
    <TestMethod>
    Public Sub ParseHandshake_EmptyFrame_ReturnsDefaults()
        Dim frame As New JObject()

        Dim info As WpfDebugHandshakeInfo = WpfDebugResultReader.ParseHandshake(frame)

        Assert.IsNotNull(info)
        Assert.AreEqual(0, info.Pid)
        Assert.IsNull(info.ProtocolVersion)
        Assert.IsNull(info.MainWindowTitle)
    End Sub

    ''' <summary>握手帧带 processPath token 时应解析到 ProcessPath 字段。</summary>
    <TestMethod>
    Public Sub ParseHandshake_WithProcessPath_ParsesPath()
        Dim frame As New JObject()
        frame("protocolVersion") = "1"
        frame("pid") = 5599
        frame("processPath") = "C:\Apps\MyWpfApp.exe"

        Dim info As WpfDebugHandshakeInfo = WpfDebugResultReader.ParseHandshake(frame)

        Assert.IsNotNull(info)
        Assert.AreEqual("C:\Apps\MyWpfApp.exe", info.ProcessPath)
        Assert.AreEqual(5599, info.Pid)
    End Sub
End Class

''' <summary>
''' WpfDebugConnection（握手信息容器）构造测试。
''' 无副作用：纯 POCO 构造。
''' </summary>
<TestClass>
Public Class WpfDebugConnectionTests

    ''' <summary>从握手信息构造连接快照，三个字段透传正确。</summary>
    <TestMethod>
    Public Sub New_FromHandshake_ExposesFields()
        Dim handshake As New WpfDebugHandshakeInfo With {
            .ProtocolVersion = "1",
            .Pid = 4321,
            .MainWindowTitle = "Target Window"
        }

        Dim connection As New WpfDebugConnection(handshake)

        Assert.AreEqual("1", connection.ProtocolVersion)
        Assert.AreEqual(4321, connection.Pid)
        Assert.AreEqual("Target Window", connection.MainWindowTitle)
    End Sub

    ''' <summary>握手信息带 ProcessPath 时应透传暴露到 WpfDebugConnection.ProcessPath。</summary>
    <TestMethod>
    Public Sub New_FromHandshake_ExposesProcessPath()
        Dim handshake As New WpfDebugHandshakeInfo With {
            .ProtocolVersion = "1",
            .Pid = 8812,
            .MainWindowTitle = "App",
            .ProcessPath = "C:\Apps\TargetApp.exe"
        }

        Dim connection As New WpfDebugConnection(handshake)

        Assert.AreEqual("C:\Apps\TargetApp.exe", connection.ProcessPath)
        Assert.AreEqual(8812, connection.Pid)
    End Sub

    ''' <summary>Nothing 握手信息应抛 ArgumentNullException。</summary>
    <TestMethod>
    Public Sub New_NullHandshake_Throws()
        Try
            Dim c As New WpfDebugConnection(Nothing)
            Assert.Fail("Nothing 握手信息不应被接受")
        Catch ex As ArgumentNullException
            ' 预期
        End Try
    End Sub

    ''' <summary>标题为空的握手信息应被接受（被控端握手时窗口可能未建好）。</summary>
    <TestMethod>
    Public Sub New_NullTitle_IsAccepted()
        Dim handshake As New WpfDebugHandshakeInfo With {
            .ProtocolVersion = "1",
            .Pid = 1,
            .MainWindowTitle = Nothing
        }

        Dim connection As New WpfDebugConnection(handshake)

        Assert.IsNull(connection.MainWindowTitle)
        Assert.AreEqual(1, connection.Pid)
    End Sub
End Class
