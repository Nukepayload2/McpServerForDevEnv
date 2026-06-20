Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports Newtonsoft.Json

' Length-prefixed framing 辅助：4 字节大端长度 + UTF-8 JSON 负载。
' 主控↔被控端 named pipe 上的消息帧。多目标兼容（net472 与 net8.0 均可用）。

''' <summary>
''' length-prefixed 帧的读写辅助。把对象 JSON 序列化成 UTF-8，前置 4 字节大端长度写入；
''' 读取时先读 4 字节长度再读 JSON 反序列化。
''' </summary>
Public NotInheritable Class MessageFramer

    ' 单帧负载上限（64 MiB），防止恶意/损坏的长度字段导致一次性分配超大缓冲。
    Public Const MaxPayloadBytes As Integer = 64 * 1024 * 1024

    Private Sub New()
    End Sub

    ''' <summary>
    ''' 把 <paramref name="payload"/> JSON 序列化为 UTF-8，前置 4 字节大端长度写入流。
    ''' </summary>
    Public Shared Async Function WriteAsync(stream As Stream, payload As Object, Optional cancellationToken As CancellationToken = Nothing) As Task
        If stream Is Nothing Then Throw New ArgumentNullException(NameOf(stream))
        If payload Is Nothing Then Throw New ArgumentNullException(NameOf(payload))

        Dim json As String = JsonConvert.SerializeObject(payload)
        Dim body As Byte() = Encoding.UTF8.GetBytes(json)

        Dim header As Byte() = New Byte(3) {}
        header(0) = CByte(body.Length >> 24)
        header(1) = CByte(body.Length >> 16)
        header(2) = CByte(body.Length >> 8)
        header(3) = CByte(body.Length)

        Await stream.WriteAsync(header, 0, header.Length, cancellationToken).ConfigureAwait(False)
        Await stream.WriteAsync(body, 0, body.Length, cancellationToken).ConfigureAwait(False)
        Await stream.FlushAsync(cancellationToken).ConfigureAwait(False)
    End Function

    ''' <summary>
    ''' 读取一帧并反序列化为 <typeparamref name="T"/>。先读 4 字节大端长度，再读对应字节 JSON。
    ''' 流末尾（无更多帧）返回 Nothing。
    ''' </summary>
    Public Shared Async Function ReadAsync(Of T As Class)(stream As Stream, Optional cancellationToken As CancellationToken = Nothing) As Task(Of T)
        If stream Is Nothing Then Throw New ArgumentNullException(NameOf(stream))

        Dim header As Byte() = New Byte(3) {}
        If Not Await ReadExactAsync(stream, header, 0, header.Length, cancellationToken).ConfigureAwait(False) Then
            ' 连接已关闭，没有更多帧。
            Return Nothing
        End If

        Dim length As Integer = (header(0) << 24) Or (header(1) << 16) Or (header(2) << 8) Or header(3)
        If length < 0 OrElse length > MaxPayloadBytes Then
            Throw New InvalidDataException($"收到的帧长度非法：{length}")
        End If

        Dim body As Byte() = New Byte(length - 1) {}
        If length > 0 Then
            If Not Await ReadExactAsync(stream, body, 0, length, cancellationToken).ConfigureAwait(False) Then
                Throw New EndOfStreamException("帧体未读完连接即关闭")
            End If
        End If

        Dim json As String = Encoding.UTF8.GetString(body)
        Return JsonConvert.DeserializeObject(Of T)(json)
    End Function

    ''' <summary>
    ''' 从流中精确读取 <paramref name="count"/> 字节到 buffer。
    ''' 在读到任何字节之前就遇到 EOF（干净关闭）返回 False；读到一半遇到 EOF 抛
    ''' <see cref="EndOfStreamException"/>。
    ''' </summary>
    Private Shared Async Function ReadExactAsync(stream As Stream, buffer As Byte(), offset As Integer, count As Integer, cancellationToken As CancellationToken) As Task(Of Boolean)
        Dim total As Integer = 0
        While total < count
            Dim read As Integer = Await stream.ReadAsync(buffer, offset + total, count - total, cancellationToken).ConfigureAwait(False)
            If read = 0 Then
                If total = 0 Then
                    ' 连接干净关闭，没有更多帧。
                    Return False
                End If
                Throw New EndOfStreamException("帧未读完连接即关闭")
            End If
            total += read
        End While
        Return True
    End Function
End Class
