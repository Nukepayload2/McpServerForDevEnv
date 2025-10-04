Partial Public Class CustomMessageBox
    Inherits Window

    Public Enum MessageBoxType
        Information
        Warning
        [Error]
        Question
    End Enum

    Public Enum MessageBoxResult
        None
        OK
        Yes
        No
    End Enum

    Private _result As MessageBoxResult = MessageBoxResult.None
    Private _messageType As MessageBoxType

    Public Sub New(title As String, message As String, type As MessageBoxType, Optional showYesNo As Boolean = False)
        InitializeComponent()
        Me.Title = title
        Me.WindowStartupLocation = WindowStartupLocation.CenterOwner
        _messageType = type

        SetupMessage(message, type, showYesNo)
    End Sub

    Private Sub PlaySystemSound()
        Select Case _messageType
            Case MessageBoxType.Error
                System.Media.SystemSounds.Beep.Play()
            Case MessageBoxType.Warning
                System.Media.SystemSounds.Exclamation.Play()
            Case MessageBoxType.Information
                System.Media.SystemSounds.Asterisk.Play()
            Case MessageBoxType.Question
                System.Media.SystemSounds.Question.Play()
        End Select
    End Sub

    Private Sub SetupMessage(message As String, type As MessageBoxType, showYesNo As Boolean)
        ' Set icon and message text
        Select Case type
            Case MessageBoxType.Information
                IconTextBlock.Text = "i"
                IconTextBlock.Foreground = Brushes.Blue
            Case MessageBoxType.Warning
                IconTextBlock.Text = "!"
                IconTextBlock.Foreground = Brushes.Orange
            Case MessageBoxType.Error
                IconTextBlock.Text = "X"
                IconTextBlock.Foreground = Brushes.Red
            Case MessageBoxType.Question
                IconTextBlock.Text = "?"
                IconTextBlock.Foreground = Brushes.Blue
        End Select

        MessageTextBlock.Text = message

        ' Set button visibility
        If showYesNo Then
            YesButton.Visibility = Visibility.Visible
            NoButton.Visibility = Visibility.Visible
            OKButton.Visibility = Visibility.Collapsed
        Else
            YesButton.Visibility = Visibility.Collapsed
            NoButton.Visibility = Visibility.Collapsed
            OKButton.Visibility = Visibility.Visible
        End If
    End Sub

    Private Sub YesButton_Click(sender As Object, e As RoutedEventArgs)
        Me.DialogResult = True
        _result = MessageBoxResult.Yes
        Me.Close()
    End Sub

    Private Sub NoButton_Click(sender As Object, e As RoutedEventArgs)
        Me.DialogResult = False
        _result = MessageBoxResult.No
        Me.Close()
    End Sub

    Private Sub OKButton_Click(sender As Object, e As RoutedEventArgs)
        Me.DialogResult = True
        _result = MessageBoxResult.OK
        Me.Close()
    End Sub

    Public Overloads Function ShowDialog(owner As Window) As MessageBoxResult
        Me.Owner = owner
        PlaySystemSound()
        MyBase.ShowDialog()
        Return _result
    End Function

    Public Overloads Shared Function Show(owner As Window, message As String, title As String, type As MessageBoxType, Optional showYesNo As Boolean = False, Optional isTopmost As Boolean = False) As MessageBoxResult
        Dim msgBox As New CustomMessageBox(title, message, type, showYesNo)
        msgBox.Owner = owner
        msgBox.Topmost = isTopmost
        msgBox.PlaySystemSound()
        msgBox.ShowDialog()
        Return msgBox._result
    End Function
End Class