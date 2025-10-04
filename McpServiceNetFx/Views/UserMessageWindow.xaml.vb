Partial Public Class UserMessageWindow
    Inherits Window

    Public Sub New(title As String, message As String, command As String)
        InitializeComponent()
        Me.Title = title
        Me.WindowStartupLocation = WindowStartupLocation.CenterScreen

        SetupContent(message, command)
    End Sub

    Private Sub SetupContent(message As String, command As String)
        MessageTextBlock.Text = message
        CommandTextBox.Text = command
    End Sub

    Private Sub CloseButton_Click() Handles CloseButton.Click
        Me.Close()
    End Sub
End Class