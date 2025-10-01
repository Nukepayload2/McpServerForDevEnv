Imports EnvDTE80

''' <summary>
''' 异步工具窗口状态对象，包含在后台线程初始化的资源
''' </summary>
Public Class ErrorListToolWindowState
    Public Property DTE As DTE2
    Public Property WindowTitle As String = "编译错误列表 (JSON)"
End Class