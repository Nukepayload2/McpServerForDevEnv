
Imports System.ComponentModel

Class PermissionItem
    Implements INotifyPropertyChanged

    Public Property FeatureName As String
    Public Property Description As String
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
End Class

Public Enum PermissionLevel
    Allow
    Ask
    Deny
End Enum

Public Class LogEntry
    Public Property Timestamp As DateTime
    Public Property Operation As String
    Public Property Result As String
    Public Property Details As String
End Class

Public Class FeaturePermission
    Public Property FeatureName As String
    Public Property Description As String
    Public Property Permission As PermissionLevel
End Class
