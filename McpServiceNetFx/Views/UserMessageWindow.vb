Public Class UserMessageWindow
    Inherits Window

    Public Sub New(title As String, message As String, command As String)
        Me.Title = title
        Me.Width = 500
        Me.Height = 300
        Me.WindowStartupLocation = WindowStartupLocation.CenterScreen
        Me.ResizeMode = ResizeMode.CanResize

        CreateLayout(message, command)
    End Sub

    Private Sub CreateLayout(message As String, command As String)
        Dim mainPanel As New StackPanel() With {
            .Margin = New Thickness(8)
        }

        ' 消息文本
        Dim messageLabel As New Label() With {
            .Content = "提示信息:",
            .FontWeight = FontWeights.Bold,
            .Margin = New Thickness(0, 0, 0, 4)
        }
        mainPanel.Children.Add(messageLabel)

        Dim messageTextBlock As New TextBlock() With {
            .Text = message,
            .TextWrapping = TextWrapping.Wrap,
            .Margin = New Thickness(0, 0, 0, 8)
        }
        mainPanel.Children.Add(messageTextBlock)

        ' 命令文本框（只读）
        Dim commandTextBox As New TextBox() With {
            .Text = command,
            .IsReadOnly = True,
            .TextWrapping = TextWrapping.Wrap,
            .VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            .FontFamily = New FontFamily("Consolas"),
            .Margin = New Thickness(0, 0, 0, 8)
        }
        mainPanel.Children.Add(commandTextBox)

        ' 按钮面板
        Dim buttonPanel As New StackPanel() With {
            .Orientation = Orientation.Horizontal,
            .HorizontalAlignment = HorizontalAlignment.Right,
            .Margin = New Thickness(0, 8, 0, 0)
        }

        ' 关闭按钮
        Dim closeButton As New Button() With {
            .Content = "关闭",
            .Width = 80,
            .Margin = New Thickness(8, 0, 0, 0),
            .IsCancel = True
        }
        AddHandler closeButton.Click, Sub(s, e) Me.Close()
        buttonPanel.Children.Add(closeButton)

        mainPanel.Children.Add(buttonPanel)

        ' 设置内容
        Me.Content = mainPanel
    End Sub
End Class