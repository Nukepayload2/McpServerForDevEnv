Imports System.Windows.Navigation

''' <summary>
''' 权限确认对话框结果
''' </summary>
Public Enum PermissionConfirmResult
    ''' <summary>
    ''' 允许（不配置策略）
    ''' </summary>
    Allow

    ''' <summary>
    ''' 拒绝（不配置策略）
    ''' </summary>
    Deny

    ''' <summary>
    ''' 允许并配置允许策略
    ''' </summary>
    AllowWithPolicy

    ''' <summary>
    ''' 拒绝并配置拒绝策略
    ''' </summary>
    DenyWithPolicy
End Enum

''' <summary>
''' 权限确认对话框
''' 用于确认文件操作权限，并允许用户快速配置路径策略
''' </summary>
Partial Public Class PermissionConfirmDialog
    Private _result As PermissionConfirmResult
    Private _policyPattern As String = String.Empty

    ''' <summary>
    ''' 创建权限确认对话框
    ''' </summary>
    Public Sub New()
        InitializeComponent()
    End Sub

    ''' <summary>
    ''' 设置对话框内容
    ''' </summary>
    ''' <param name="featureName">功能名称</param>
    ''' <param name="operationDescription">操作描述</param>
    ''' <param name="filePath">文件路径</param>
    Public Sub SetContent(featureName As String, operationDescription As String, filePath As String)
        TxtFeatureName.Text = featureName
        TxtOperationDescription.Text = operationDescription
        TxtFilePath.Text = filePath

        ' 预填充模式输入框
        TxtAllowPattern.Text = filePath
        TxtDenyPattern.Text = filePath
    End Sub

    ''' <summary>
    ''' 获取对话框结果
    ''' </summary>
    ''' <returns>权限确认结果</returns>
    Public Function GetResult() As PermissionConfirmResult
        Return _result
    End Function

    ''' <summary>
    ''' 获取策略模式（如果配置了策略）
    ''' </summary>
    ''' <returns>策略模式字符串</returns>
    Public Function GetPolicyPattern() As String
        Return _policyPattern
    End Function

    ''' <summary>
    ''' 允许按钮点击
    ''' </summary>
    Private Sub OnAllowClick()
        If RbAlwaysAllow.IsChecked.Value Then
            _result = PermissionConfirmResult.AllowWithPolicy
            _policyPattern = TxtAllowPattern.Text
        ElseIf RbAlwaysDeny.IsChecked.Value Then
            ' 逻辑错误：拒绝按钮点击但选择了总是拒绝
            _result = PermissionConfirmResult.DenyWithPolicy
            _policyPattern = TxtDenyPattern.Text
        Else
            _result = PermissionConfirmResult.Allow
            _policyPattern = String.Empty
        End If

        Me.DialogResult = True
        Me.Close()
    End Sub

    ''' <summary>
    ''' 拒绝按钮点击
    ''' </summary>
    Private Sub OnDenyClick()
        If RbAlwaysDeny.IsChecked.Value Then
            _result = PermissionConfirmResult.DenyWithPolicy
            _policyPattern = TxtDenyPattern.Text
        ElseIf RbAlwaysAllow.IsChecked.Value Then
            ' 逻辑错误：允许按钮点击但选择了总是允许
            _result = PermissionConfirmResult.AllowWithPolicy
            _policyPattern = TxtAllowPattern.Text
        Else
            _result = PermissionConfirmResult.Deny
            _policyPattern = String.Empty
        End If

        Me.DialogResult = False
        Me.Close()
    End Sub

    ''' <summary>
    ''' 了解更多链接点击
    ''' </summary>
    Private Sub OnLearnMoreClick(sender As Object, e As RequestNavigateEventArgs)
        Try
            Dim url = My.Resources.LearnMore_Url
            Process.Start(New ProcessStartInfo(url) With {
                .UseShellExecute = True
            })
        Catch ex As Exception
            UtilityModule.ShowError(Me, String.Format(My.Resources.MsgCannotOpenHelpDoc, ex.Message))
        End Try
    End Sub
End Class
