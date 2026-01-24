
Imports System.ComponentModel

Public Class PermissionItem
    Implements INotifyPropertyChanged

    Public Property FeatureName As String
    Public Property Description As String
    Public Property IsFileTool As Boolean
    Private _permission As PermissionLevel

    Public Property Permission As PermissionLevel
        Get
            Return _permission
        End Get
        Set
            _permission = Value
            OnPropertyChanged(NameOf(Permission))
        End Set
    End Property

    Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

    Protected Overridable Sub OnPropertyChanged(name As String)
        RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(name))
    End Sub

    ''' <summary>
    ''' 获取此工具可用的权限级别集合
    ''' 文件工具返回 ForFileTools（含 AlwaysAsk），其他工具返回 All
    ''' </summary>
    Public ReadOnly Property AvailablePermissionLevels As PermissionLevel()
        Get
            If IsFileTool Then
                Return PermissionLevels.ForFileTools
            Else
                Return PermissionLevels.All
            End If
        End Get
    End Property
End Class

Public Enum PermissionLevel
    Allow
    Ask
    AlwaysAsk
    Deny
End Enum

Public Class PermissionLevels
    ''' <summary>
    ''' 所有工具的权限级别（不含 AlwaysAsk）
    ''' </summary>
    Public Shared ReadOnly Property All As PermissionLevel() = {
        PermissionLevel.Allow,
        PermissionLevel.Ask,
        PermissionLevel.Deny
    }

    ''' <summary>
    ''' 文件工具的权限级别（含 AlwaysAsk）
    ''' </summary>
    Public Shared ReadOnly Property ForFileTools As PermissionLevel() = {
        PermissionLevel.Allow,
        PermissionLevel.Ask,
        PermissionLevel.AlwaysAsk,
        PermissionLevel.Deny
    }

    ''' <summary>
    ''' 兼容旧代码的属性（返回 All）
    ''' </summary>
    Public Shared ReadOnly Property Value As PermissionLevel() = All
End Class

''' <summary>
''' 文件访问类型集合
''' 用于 UI 绑定
''' </summary>
Public Class FileAccessTypes
    ''' <summary>
    ''' 所有文件访问类型
    ''' </summary>
    Public Shared ReadOnly Property Value As FileAccessType() = {
        FileAccessType.Read,
        FileAccessType.Write,
        FileAccessType.ReadWrite
    }
End Class

Public Class LogEntry
    Public Property Timestamp As DateTime
    Public Property Operation As String
    Public Property Result As String
    Public Property Details As String
End Class

Public Class ServerConfiguration
    Public Property Port As Integer = 38080
End Class
