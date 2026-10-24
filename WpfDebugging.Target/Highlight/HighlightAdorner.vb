Imports System.Windows
Imports System.Windows.Documents
Imports System.Windows.Media

' Snoop 风格的选中高亮：在目标元素上叠一个红色描边，几秒后自动移除。
'
' 高亮叠在装饰层，不改元素自身属性：改 BorderThickness 会触发布局、覆盖宿主样式，
' 并让业务逻辑读到被改过的 RenderSize。

''' <summary>给任意 <see cref="UIElement"/> 画一圈描边的装饰层。</summary>
Public Class HighlightAdorner
    Inherits Adorner

    Private ReadOnly _pen As Pen

    Public Sub New(adornedElement As UIElement, color As Color, thickness As Double)
        MyBase.New(adornedElement)

        Dim brush As New SolidColorBrush(color)
        brush.Freeze()
        _pen = New Pen(brush, thickness)
        _pen.Freeze()

        IsHitTestVisible = False
    End Sub

    Protected Overrides Sub OnRender(drawingContext As DrawingContext)
        MyBase.OnRender(drawingContext)
        If drawingContext Is Nothing Then Return

        Dim size As Size = AdornedElement.RenderSize
        If size.Width <= 0 OrElse size.Height <= 0 Then Return

        ' 内缩半个线宽，避免描边被裁剪
        Dim half As Double = _pen.Thickness / 2
        Dim rect As New Rect(half, half,
                             Math.Max(0, size.Width - _pen.Thickness),
                             Math.Max(0, size.Height - _pen.Thickness))
        drawingContext.DrawRectangle(Nothing, _pen, rect)
    End Sub
End Class
