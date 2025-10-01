Imports System
Imports System.Runtime.InteropServices
Imports System.Windows.Controls
Imports Microsoft.VisualStudio.Shell
Imports Microsoft.VisualStudio.Imaging
Imports Microsoft.VisualStudio.Imaging.Interop

''' <summary>
''' 符号搜索工具窗口
''' </summary>
<Guid("E86B2AEF-5348-4365-915B-29966CE14D78")>
    Public Class SymbolSearchToolWindow
        Inherits ToolWindowPane

        Private _control As SymbolSearchControl

        ''' <summary>
        ''' 构造函数
        ''' </summary>
        Public Sub New()
            MyBase.New(Nothing)

            ' 设置窗口标题
            Caption = "LSP 符号搜索"

            ' 设置窗口图标
            BitmapImageMoniker = KnownMonikers.Search

            ' 创建用户控件
            _control = New SymbolSearchControl()
            Content = _control
        End Sub

        ''' <summary>
        ''' 获取工具窗口控件
        ''' </summary>
        Public ReadOnly Property Control As SymbolSearchControl
            Get
                Return _control
            End Get
        End Property
    End Class
