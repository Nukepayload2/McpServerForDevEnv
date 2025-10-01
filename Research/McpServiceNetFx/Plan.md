# Visual Studio DTE2 控制器

## 特点
- 选择 Visual Studio 实例：有实例表和搜索功能，实例表显示 窗口标题(DTE.MainWindow.Caption),解决方案路径(DTE.Solution.FullName) 列。
- MCP 功能授权：列出支持的功能，配置：允许、询问、禁用。预设：全部允许，全部询问，自定义。要持久化自定义配置。
- MCP 功能包含：获取当前解决方案路径、列出当前解决方案内的项目、编译当前解决方案、按项目名称编译指定项目、获取错误列表。注意不能包含其它功能。
- MCP 服务配置：填写端口号、启动服务、关闭服务。附加的 Visual Studio 退出时自动关闭服务。
- 日志：记录服务开启后同意了哪些请求的操作，显示在表格中，支持持久化（导出日志）

## 技术要求
- 持久化通过 XML 文件记录到用户 %LocalAppData%\当前程序集名称 实现，使用 VB 自带的 XML LINQ 语法
- 使用 x:Name 配合 Handles 子句组成 CodeBehind 模式
- 控件命名采取旧版 VB 风格，例如 BtnClick, BtnOk, DgLogs, DgVsInstances
- 工具方法用模块包装
- 所有类和模块写在最外层，扁平化命名空间
- 使用 DispatcherTimer 保证定时操作的线程安全
- 使用 DispatcherTimer 对搜索做防抖处理
- 利用 Partial 类分隔 MainWindow 的业务逻辑，例如 MainWindow.FeaturePermissions.vb, MainWindow.Logging.vb, MainWindow.McpService.vb

## 知识

### 列出 Visual Studio 实例
这是示例代码，从中提取有价值的信息实现列出实例的功能

```vbnet
' COM interop 声明
<DllImport("ole32.dll")>
Private Shared Function GetRunningObjectTable(reserved As Integer, ByRef prot As IRunningObjectTable) As Integer
End Function

<DllImport("ole32.dll")>
Private Shared Function CreateBindCtx(reserved As Integer, ByRef ppbc As IBindCtx) As Integer
End Function

Public Sub New(logger As ILogger)
    _logger = logger
End Sub

''' <summary>
''' 获取所有当前运行的 Visual Studio 实例
''' </summary>
''' <returns>VisualStudioInstance 对象数组</returns>
Public Function GetRunningInstances() As VisualStudioInstance()
    Try
        Dim instances As New List(Of VisualStudioInstance)()
        
        Dim rot As IRunningObjectTable = Nothing
        Dim hr As Integer = GetRunningObjectTable(0, rot)
        
        If hr <> 0 Then
            Throw New COMException($"获取运行对象表失败。HRESULT: {hr:X}")
        End If
        
        instances.AddRange(EnumerateVisualStudioInstances(rot))
        Return instances.ToArray()
        
    Catch ex As Exception
        _logger?.LogError(ex, "获取运行中的 Visual Studio 实例时出错")
        Throw
    End Try
End Function

''' <summary>
''' 枚举运行对象表中的 Visual Studio 实例
''' </summary>
Private Function EnumerateVisualStudioInstances(rot As IRunningObjectTable) As VisualStudioInstance()
    Dim instances As New List(Of VisualStudioInstance)()
    
    Try
        Dim enumMoniker As IEnumMoniker = Nothing
        rot.EnumRunning(enumMoniker)
        
        If enumMoniker IsNot Nothing Then
            Dim monikers(0) As IMoniker
            Dim fetched As IntPtr = IntPtr.Zero
            
            While enumMoniker.Next(1, monikers, fetched) = 0
                Dim moniker As IMoniker = monikers(0)
                If moniker IsNot Nothing Then
                    Try
                        Dim instance As VisualStudioInstance = ProcessMoniker(moniker)
                        If instance IsNot Nothing Then
                            instances.Add(instance)
                            _logger?.LogDebug("找到 VS 实例: PID={ProcessId}, Version={Version}", 
                                instance.ProcessId, instance.Version)
                        End If
                    Finally
                        If moniker IsNot Nothing Then
                            Marshal.ReleaseComObject(moniker)
                        End If
                    End Try
                End If
            End While
        End If
        
        _logger?.LogInformation("找到 {Count} 个 Visual Studio 实例", instances.Count)
        Return instances.ToArray()
        
    Catch ex As Exception
        _logger?.LogError(ex, "枚举 Visual Studio 实例时出错")
        Throw
    End Try
End Function
```

### 获取 VS 的信息
- 窗口标题 DTE.MainWindow.Caption
- 解决方案路径 DTE.Solution.FullName

### 检测 VS 存活状态

检查正常退出：

```vbnet
Private Sub SetupDTEEvents(dte2 As DTE2)
    Try
        ' 监听 DTE 事件
        AddHandler dte2.Events.DTEEvents.OnBeginShutdown, AddressOf OnVisualStudioShutdown
    Catch ex As Exception
        ' 如果无法添加事件处理器，说明 DTE 可能已失效
        HandleDTEDisconnected()
    End Try
End Sub
```

检查异常退出：检查主窗口是否存活

```vbnet
Public Class VisualStudioMonitor
      Private ReadOnly dte2 As DTE2
      Private ReadOnly cachedHWnd As IntPtr
      Private ReadOnly originalCaption As String
      Private timer As Timer

      <DllImport("user32.dll", SetLastError:=True)>
      Private Shared Function IsWindow(hWnd As IntPtr) As Boolean
      End Function

      <DllImport("user32.dll", SetLastError:=True)>
      Private Shared Function GetWindowText(hWnd As IntPtr, lpString As StringBuilder, nMaxCount
  As Integer) As Integer
      End Function

      Public Sub New(dteInstance As DTE2)
          dte2 = dteInstance
          cachedHWnd = New IntPtr(dte2.MainWindow.HWnd)
          originalCaption = dte2.MainWindow.Caption
      End Sub

      Private Sub CheckVisualStudioStatus(sender As Object, e As ElapsedEventArgs)
          Dim isExited As Boolean = False

          ' 检查窗口句柄是否有效
          If Not IsWindow(cachedHWnd) Then
              isExited = True
          End If

          ' 检查窗口标题是否匹配（防止句柄被重用）
          If Not isExited Then
              Try
                  Dim caption As String = GetWindowTitle(cachedHWnd)
                  If String.IsNullOrEmpty(caption) OrElse Not caption.Contains("Visual Studio") Then
                      isExited = True
                  End If
              Catch ex As Exception
                  isExited = True
              End Try
          End If

          If isExited Then
              timer.Stop()
              OnVisualStudioExited()
          End If
      End Sub

      Private Function GetWindowTitle(hWnd As IntPtr) As String
          Dim builder As New StringBuilder(256)
          GetWindowText(hWnd, builder, builder.Capacity)
          Return builder.ToString()
      End Sub

      Private Sub OnVisualStudioExited()
          Console.WriteLine("Visual Studio 已退出")
      End Sub
  End Class
```

把这两种办法结合起来就能以出色的性能检查 Visual Studio 是否退出