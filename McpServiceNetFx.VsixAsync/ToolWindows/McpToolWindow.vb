Imports System
Imports System.Collections.Generic
Imports System.Runtime.InteropServices
Imports Microsoft.VisualStudio.Imaging
Imports Microsoft.VisualStudio.Shell

Namespace ToolWindows
    ''' <summary>
    ''' MCP 工具窗口类
    ''' </summary>
    <Guid("c7b4a4f9-2d6e-4f8b-9e5a-6d7c8e9f0a1c")>
    Public Class McpToolWindow
        Inherits ToolWindowPane

        Public Const WindowGuidString As String = "c7b4a4f9-2d6e-4f8b-9e5a-6d7c8e9f0a1c"
        Public Const Title As String = "MCP 服务管理器"

        Private _state As McpWindowState

        Public Sub New()
            MyBase.New()
            Caption = Title
            BitmapImageMoniker = KnownMonikers.Settings
            _state = New ToolWindows.McpWindowState()

            ' 创建WPF控件
            Content = New McpToolWindowControl(_state)
        End Sub

        ''' <summary>
        ''' 获取窗口状态
        ''' </summary>
        Public ReadOnly Property State As McpWindowState
            Get
                Return _state
            End Get
        End Property
    End Class
End Namespace