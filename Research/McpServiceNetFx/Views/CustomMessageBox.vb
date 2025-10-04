Public Class CustomMessageBox
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
        Me.Title = title
        Me.Width = 400
        Me.Height = 200
        Me.WindowStartupLocation = WindowStartupLocation.CenterOwner
        Me.ResizeMode = ResizeMode.NoResize
        Me.ShowInTaskbar = False
        _messageType = type

        CreateLayout(message, type, showYesNo)
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

    Private Sub CreateLayout(message As String, type As MessageBoxType, showYesNo As Boolean)
        Dim mainPanel As New Grid() With {
            .Margin = New Thickness(20)
        }

        ' Define rows
        mainPanel.RowDefinitions.Add(New RowDefinition() With {.Height = GridLength.Auto})
        mainPanel.RowDefinitions.Add(New RowDefinition() With {.Height = New GridLength(20)})
        mainPanel.RowDefinitions.Add(New RowDefinition() With {.Height = GridLength.Auto})

        ' Message panel
        Dim messagePanel As New StackPanel() With {
            .Orientation = Orientation.Horizontal,
            .VerticalAlignment = VerticalAlignment.Center
        }
        Grid.SetRow(messagePanel, 0)

        ' Add icon
        Dim icon As New TextBlock() With {
            .FontSize = 32,
            .Margin = New Thickness(0, 0, 15, 0),
            .VerticalAlignment = VerticalAlignment.Center
        }

        Select Case type
            Case MessageBoxType.Information
                icon.Text = "i"
                icon.Foreground = Brushes.Blue
            Case MessageBoxType.Warning
                icon.Text = "!"
                icon.Foreground = Brushes.Orange
            Case MessageBoxType.Error
                icon.Text = "X"
                icon.Foreground = Brushes.Red
            Case MessageBoxType.Question
                icon.Text = "?"
                icon.Foreground = Brushes.Blue
        End Select

        messagePanel.Children.Add(icon)

        ' Add message text
        Dim messageText As New TextBlock() With {
            .Text = message,
            .TextWrapping = TextWrapping.Wrap,
            .MaxWidth = 300,
            .VerticalAlignment = VerticalAlignment.Center
        }
        messagePanel.Children.Add(messageText)

        mainPanel.Children.Add(messagePanel)

        ' Button panel
        Dim buttonPanel As New StackPanel() With {
            .Orientation = Orientation.Horizontal,
            .HorizontalAlignment = HorizontalAlignment.Right
        }
        Grid.SetRow(buttonPanel, 2)

        If showYesNo Then
            Dim yesButton As New Button() With {
                .Content = "是",
                .Width = 80,
                .Margin = New Thickness(0, 0, 10, 0),
                .IsDefault = True
            }
            AddHandler yesButton.Click, Sub(s, e)
                                            Me.DialogResult = True
                                            Me.Close()
                                        End Sub
            buttonPanel.Children.Add(yesButton)

            Dim noButton As New Button() With {
                .Content = "否",
                .Width = 80,
                .IsCancel = True
            }
            AddHandler noButton.Click, Sub(s, e)
                                           Me.DialogResult = False
                                           Me.Close()
                                       End Sub
            buttonPanel.Children.Add(noButton)
        Else
            Dim okButton As New Button() With {
                .Content = "确定",
                .Width = 80,
                .IsDefault = True,
                .IsCancel = True
            }
            AddHandler okButton.Click, Sub(s, e)
                                           Me.DialogResult = True
                                           Me.Close()
                                       End Sub
            buttonPanel.Children.Add(okButton)
        End If

        mainPanel.Children.Add(buttonPanel)

        ' Set content
        Me.Content = mainPanel
    End Sub

    Public Overloads Function ShowDialog(owner As Window) As MessageBoxResult
        Me.Owner = owner
        PlaySystemSound()
        MyBase.ShowDialog()
        Return _result
    End Function

    Public Overloads Shared Function Show(owner As Window, message As String, title As String, type As MessageBoxType, Optional showYesNo As Boolean = False) As MessageBoxResult
        Dim msgBox As New CustomMessageBox(title, message, type, showYesNo)
        msgBox.Owner = owner
        msgBox.PlaySystemSound()
        msgBox.ShowDialog()
        If msgBox.DialogResult = True Then
            Return MessageBoxResult.OK
        ElseIf showYesNo Then
            Return MessageBoxResult.No
        Else
            Return MessageBoxResult.OK
        End If
    End Function
End Class