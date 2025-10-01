Imports System.Runtime.InteropServices
Imports System.Text
Imports Microsoft.VisualStudio.Interop
Imports System.Runtime.InteropServices.ComTypes
Imports EnvDTE80

Public Class VisualStudioInstance
    Public Property Caption As String
    Public Property SolutionPath As String
    Public Property Version As String
    Public Property ProcessId As Integer
    Public Property DTE2 As EnvDTE80.DTE2
End Class

Public Module VisualStudioEnumerator
    <DllImport("ole32.dll")>
    Private Function GetRunningObjectTable(reserved As Integer, ByRef prot As IRunningObjectTable) As Integer
    End Function

    <DllImport("ole32.dll")>
    Private Function CreateBindCtx(reserved As Integer, ByRef ppbc As IBindCtx) As Integer
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function IsWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function GetWindowText(hWnd As IntPtr, lpString As StringBuilder, nMaxCount As Integer) As Integer
    End Function

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
            Throw New Exception("获取运行中的 Visual Studio 实例时出错", ex)
        End Try
    End Function

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
                            Dim instance As VisualStudioInstance = ProcessMoniker(moniker, rot)
                            If instance IsNot Nothing Then
                                instances.Add(instance)
                            End If
                        Finally
                            If moniker IsNot Nothing Then
                                Marshal.ReleaseComObject(moniker)
                            End If
                        End Try
                    End If
                End While
            End If

            Return instances.ToArray()

        Catch ex As Exception
            Throw New Exception("枚举 Visual Studio 实例时出错", ex)
        End Try
    End Function

    Private Function ProcessMoniker(moniker As IMoniker, rot As IRunningObjectTable) As VisualStudioInstance
        Dim bindCtx As IBindCtx = Nothing
        Try
            CreateBindCtx(0, bindCtx)

            Dim displayName As String = GetDisplayName(moniker, bindCtx)

            ' 检查是否是 Visual Studio 实例
            If Not displayName.StartsWith("!VisualStudio.DTE.") Then
                Return Nothing
            End If

            ' 获取进程ID
            Dim parts = displayName.Split("."c)
            If parts.Length < 3 Then
                Return Nothing
            End If

            Dim processId As Integer
            If Not Integer.TryParse(parts.Last(), processId) Then
                Return Nothing
            End If

            ' 尝试获取 DTE 对象
            Dim runningObject As Object = Nothing
            Try
                rot.GetObject(moniker, runningObject)
                If runningObject Is Nothing Then
                    Return Nothing
                End If

                Dim dte2 As EnvDTE80.DTE2 = TryCast(runningObject, EnvDTE80.DTE2)
                If dte2 Is Nothing Then
                    Return Nothing
                End If

                ' 检查主窗口是否有效
                If Not IsMainWindowValid(dte2) Then
                    Return Nothing
                End If

                ' 创建实例对象
                Dim instance As New VisualStudioInstance() With {
                    .DTE2 = dte2,
                    .ProcessId = processId,
                    .Version = GetVisualStudioVersion(dte2),
                    .Caption = GetSafeCaption(dte2),
                    .SolutionPath = GetSafeSolutionPath(dte2)
                }

                Return instance

            Catch ex As Exception
                ' 无法获取 DTE 对象，可能是权限问题或实例已失效
                Return Nothing
            End Try

        Finally
            If bindCtx IsNot Nothing Then
                Marshal.ReleaseComObject(bindCtx)
            End If
        End Try
    End Function

    Private Function GetDisplayName(moniker As IMoniker, bindCtx As IBindCtx) As String
        Dim ppszDisplayName As IntPtr = IntPtr.Zero
        Try
            moniker.GetDisplayName(bindCtx, Nothing, ppszDisplayName)
            Return Marshal.PtrToStringBSTR(ppszDisplayName)
        Finally
            If ppszDisplayName <> IntPtr.Zero Then
                Marshal.FreeBSTR(ppszDisplayName)
            End If
        End Try
    End Function

    Private Function IsMainWindowValid(dte2 As EnvDTE80.DTE2) As Boolean
        Try
            If dte2.MainWindow Is Nothing Then
                Return False
            End If

            Dim hWndLong As Long = dte2.MainWindow.HWnd
            Dim hWnd As New IntPtr(hWndLong)
            If Not IsWindow(hWnd) Then
                Return False
            End If

            Dim caption As String = GetWindowTitle(hWnd)
            Return Not String.IsNullOrEmpty(caption) AndAlso caption.Contains("Visual Studio")

        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Function GetWindowTitle(hWnd As IntPtr) As String
        Dim builder As New StringBuilder(256)
        GetWindowText(hWnd, builder, builder.Capacity)
        Return builder.ToString()
    End Function

    Private Function GetVisualStudioVersion(dte2 As EnvDTE80.DTE2) As String
        Try
            Return dte2.Version
        Catch ex As Exception
            Return "未知"
        End Try
    End Function

    Private Function GetSafeCaption(dte2 As EnvDTE80.DTE2) As String
        Try
            Return dte2.MainWindow.Caption
        Catch ex As Exception
            Return "无法获取标题"
        End Try
    End Function

    Private Function GetSafeSolutionPath(dte2 As EnvDTE80.DTE2) As String
        Try
            If dte2.Solution IsNot Nothing AndAlso Not String.IsNullOrEmpty(dte2.Solution.FullName) Then
                Return dte2.Solution.FullName
            Else
                Return "无打开的解决方案"
            End If
        Catch ex As Exception
            Return "无法获取解决方案路径"
        End Try
    End Function
End Module