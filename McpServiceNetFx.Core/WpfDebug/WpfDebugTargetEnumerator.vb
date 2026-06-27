Imports System.Diagnostics
Imports System.IO
Imports McpServerForDevEnv.WpfDebugging

' WPF 调试被控端发现（主控侧）。
'
' pipe 名带 PID 后，同机可同时挂多个 WPF 被控进程。主控枚举系统 named pipe，
' 过滤出以 WpfDebugProtocol.PipeNamePrefix 开头的，解析出 PID，再用 Process.GetProcessById
' 补窗口标题/可执行路径，产候选列表供 UI 选择。
'
' 关键约束：
' 1. 只读 pipe 名 + Process 信息，**不连 pipe**（不占被控端的单连接位 maxNumberOfServerInstances=1）。
'    连接由 WpfDebugProxy 在用户选定目标后单独发起。
' 2. 所有外部查询（GetFiles / GetProcessById / MainModule.FileName）都可能因权限/位数/竞态抛，
'    单项失败兜底为对应字段空，整体不崩。
' 3. net472 / net8.0 兼容：Directory.GetFiles / Process API 在两目标均可用。

''' <summary>
''' 枚举本机所有候选 WPF 调试被控端（不连接，仅读 pipe 名 + Process 信息）。
''' </summary>
Public NotInheritable Class WpfDebugTargetEnumerator

    Private Sub New()
    End Sub

    ''' <summary>
    ''' 发现所有候选被控端：枚举 <c>\\.\pipe\</c>，过滤出 <see cref="WpfDebugProtocol.PipeNamePrefix"/> 前缀的 pipe，
    ''' 解析出 PID，用 <see cref="Process.GetProcessById"/> 查窗口标题与可执行路径。
    ''' 不连任何 pipe（不占被控端连接位）。
    ''' </summary>
    ''' <returns>候选列表（可能为空）。每项 PID 必有效，窗口标题/路径可能为空字符串。</returns>
    Public Shared Function DiscoverCandidates() As IList(Of WpfDebugTargetInfo)
        Dim results As New List(Of WpfDebugTargetInfo)()

        ' 枚举系统所有 pipe 名。失败（权限等）返回空列表，不崩。
        Dim pipeNames As String() = Nothing
        Try
            pipeNames = Directory.GetFiles("\\.\pipe\")
        Catch
            Return results
        End Try
        If pipeNames Is Nothing Then Return results

        Dim prefix As String = WpfDebugProtocol.PipeNamePrefix
        ' pipe 名形如 \\.\pipe\<前缀>.<pid>。前缀后跟 "." 分隔 PID。
        Dim prefixWithDot As String = prefix & "."

        For Each fullPipePath As String In pipeNames
            ' GetFiles 返回的是 \\.\pipe\<name> 形式，取最后一段作为 pipe 名。
            Dim pipeNameOnly As String = Path.GetFileName(fullPipePath)
            If pipeNameOnly Is Nothing Then Continue For

            ' 必须恰好以 "前缀." 开头（避免误命中前缀更长/更短的别的 pipe）。
            If Not pipeNameOnly.StartsWith(prefixWithDot, StringComparison.Ordinal) Then Continue For

            Dim pidPart As String = pipeNameOnly.Substring(prefixWithDot.Length)
            Dim pid As Integer = 0
            If Not Integer.TryParse(pidPart, pid) OrElse pid <= 0 Then Continue For

            ' PID 解析出来即视为候选 pipe；Process 信息查询失败兜底空字段，仍列入候选
            ' （主控可凭 PID 直接连，processPath/title 仅辅助选择）。
            Dim title As String = String.Empty
            Dim procPath As String = String.Empty
            Try
                Dim proc As Process = Process.GetProcessById(pid)
                Try
                    title = proc.MainWindowTitle
                Catch
                End Try
                Try
                    procPath = proc.MainModule?.FileName
                Catch
                End Try
                proc.Dispose()
            Catch
                ' 进程已退出或无权限：仍列入候选（凭 PID 可尝试连），字段为空。
            End Try

            ' 规范化：Nothing → 空字符串，避免 POCO 字段出现 Nothing。
            If title Is Nothing Then title = String.Empty
            If procPath Is Nothing Then procPath = String.Empty

            results.Add(New WpfDebugTargetInfo With {
                            .Pid = pid,
                            .MainWindowTitle = title,
                            .ProcessPath = procPath
                        })
        Next

        Return results
    End Function
End Class

''' <summary>
''' 一个候选 WPF 调试被控端（枚举产物，非握手结果）。
''' Pid 必有效；MainWindowTitle / ProcessPath 可能为空字符串（查询失败或进程无主窗口）。
''' </summary>
Public Class WpfDebugTargetInfo

    ''' <summary>被控进程 PID（来自 pipe 名解析，必有效）。</summary>
    Public Property Pid As Integer

    ''' <summary>被控进程主窗口标题（Process.MainWindowTitle，可能为空字符串）。</summary>
    Public Property MainWindowTitle As String

    ''' <summary>被控进程可执行文件全路径（Process.MainModule.FileName，可能为空字符串）。</summary>
    Public Property ProcessPath As String
End Class
