Imports System.IO
Imports System.Windows.Media
Imports System.Windows.Media.Imaging

' 截图：用 RenderTargetBitmap 渲染目标 Visual（整窗或元素区域），PngBitmapEncoder 编码成 base64。
'
' 【线程约定】本类在 UI 线程被调用（由 WpfDebugPipeServer 经 WpfDispatcher.InvokeAsync 切入），
' RenderTargetBitmap.Render 必须 UI 线程执行——正好契合。直接同步调，不二次切线程。
'
' 【OkResult 嵌套约定】本类方法只返回 ScreenshotResult/抛异常；返回值封装（包成 {"result":<JToken>}）由
' WpfDebugPipeServer.DispatchOnUiThread 的 OkResult 做。本类不碰封装。
'
' 注：本包根命名空间是 McpServerForDevEnv.WpfDebugging.Target，WPF 类型一律 Global.System.Windows.*
' 完整限定，避免「Windows」子命名空间歧义。这里 Imports 的 System.Windows.Media / .Media.Imaging 是带后缀的
' 子命名空间，不与根冲突。

''' <summary>
''' 窗口/元素截图器：把 Visual 渲染成 PNG base64 塞进 <see cref="ScreenshotResult"/>。
''' </summary>
Public NotInheritable Class WindowCapturer

    Private Sub New()
    End Sub

    ''' <summary>
    ''' 渲染整个窗口。优先渲染窗口的 Content（用户实际看到的区域），Content 不可用时退化渲染 Window 自身。
    ''' </summary>
    Public Shared Function CaptureWindow(window As Global.System.Windows.Window) As ScreenshotResult
        If window Is Nothing Then Throw New ArgumentNullException(NameOf(window))

        Dim visual As Global.System.Windows.Media.Visual =
            TryCast(window.Content, Global.System.Windows.Media.Visual)
        If visual Is Nothing Then visual = DirectCast(window, Global.System.Windows.Media.Visual)
        Return CaptureVisual(visual)
    End Function

    ''' <summary>
    ''' 渲染指定元素（按其 RenderSize）。
    ''' </summary>
    Public Shared Function CaptureElement(element As Global.System.Windows.FrameworkElement) As ScreenshotResult
        If element Is Nothing Then Throw New ArgumentNullException(NameOf(element))
        Return CaptureVisual(DirectCast(element, Global.System.Windows.Media.Visual))
    End Function

    ' 渲染单个 Visual：按其当前尺寸 + DPI 创建 RenderTargetBitmap，渲染，编码 PNG→byte[]→base64。
    Private Shared Function CaptureVisual(visual As Global.System.Windows.Media.Visual) As ScreenshotResult
        Dim size As Global.System.Windows.Size = GetRenderSize(visual)
        Dim dpi As DpiScale = GetDpi(visual)

        Dim widthPx As Integer = Math.Max(1, CInt(Math.Ceiling(size.Width * dpi.DpiScaleX)))
        Dim heightPx As Integer = Math.Max(1, CInt(Math.Ceiling(size.Height * dpi.DpiScaleY)))

        Dim bitmap As New RenderTargetBitmap(widthPx, heightPx,
                                             dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                                             PixelFormats.Pbgra32)
        bitmap.Render(visual)

        Dim pngBytes As Byte() = EncodePng(bitmap)
        Dim base64 As String = ToBase64(pngBytes)

        Return New ScreenshotResult With {
            .Width = widthPx,
            .Height = heightPx,
            .PngBase64 = base64
        }
    End Function

    ' 取 Visual 的渲染尺寸：FrameworkElement 用 ActualWidth/Height（>0 时），
    ' 否则退 RenderSize（UIElement 通用）。两者都拿不到合理值时兜底 1x1 避免 0 尺寸 RenderTargetBitmap 抛错。
    Private Shared Function GetRenderSize(visual As Global.System.Windows.Media.Visual) As Global.System.Windows.Size
        Dim fe As Global.System.Windows.FrameworkElement = TryCast(visual, Global.System.Windows.FrameworkElement)
        If fe IsNot Nothing AndAlso fe.ActualWidth > 0 AndAlso fe.ActualHeight > 0 Then
            Return New Global.System.Windows.Size(fe.ActualWidth, fe.ActualHeight)
        End If
        Dim ue As Global.System.Windows.UIElement = TryCast(visual, Global.System.Windows.UIElement)
        If ue IsNot Nothing Then
            Dim rs As Global.System.Windows.Size = ue.RenderSize
            If rs.Width > 0 AndAlso rs.Height > 0 Then Return rs
        End If
        Return New Global.System.Windows.Size(1R, 1R)
    End Function

    ' 取 Visual 所在 PresentationSource 的 DPI（像素/英寸）。拿不到（未挂到树上）退默认 96 DPI。
    Private Shared Function GetDpi(visual As Global.System.Windows.Media.Visual) As DpiScale
        Dim source As Global.System.Windows.PresentationSource = Global.System.Windows.PresentationSource.FromVisual(visual)
        If source IsNot Nothing AndAlso source.CompositionTarget IsNot Nothing Then
            Dim m As Global.System.Windows.Media.Matrix = source.CompositionTarget.TransformToDevice
            Return New DpiScale(m.M11, m.M22)
        End If
        Return New DpiScale(1R, 1R)
    End Function

    ' BitmapSource → PNG byte[]。纯内存操作（无文件/网络），可用于纯函数测试覆盖（见 ToBase64）。
    Private Shared Function EncodePng(bitmap As BitmapSource) As Byte()
        Dim encoder As New PngBitmapEncoder()
        encoder.Frames.Add(BitmapFrame.Create(bitmap))
        Using ms As New MemoryStream()
            encoder.Save(ms)
            Return ms.ToArray()
        End Using
    End Function

    ''' <summary>
    ''' byte[] → base64 字符串。纯函数、无副作用，便于单测（PNG 编码本身涉及真 UI 线程留集成验证 #24）。
    ''' </summary>
    Public Shared Function ToBase64(bytes As Byte()) As String
        If bytes Is Nothing Then Return Nothing
        Return Convert.ToBase64String(bytes, Base64FormattingOptions.None)
    End Function

    ' DPI 描述（像素/物理单位换算）。本地轻量结构，仅用 Matrix 算缩放，避免直接依赖
    ' System.Windows.DpiScale（其 API 在两个目标框架下行为差异，自己算更可控）。
    Private NotInheritable Class DpiScale

        Private ReadOnly _scaleX As Double
        Private ReadOnly _scaleY As Double

        Public Sub New(scaleX As Double, scaleY As Double)
            _scaleX = scaleX
            _scaleY = scaleY
        End Sub

        ' 相对 96 DPI 的缩放因子（1.0 = 96 DPI, 1.5 = 144 DPI ...）。
        Public ReadOnly Property DpiScaleX As Double
            Get
                Return _scaleX
            End Get
        End Property

        Public ReadOnly Property DpiScaleY As Double
            Get
                Return _scaleY
            End Get
        End Property

        ' 像素/英寸。
        Public ReadOnly Property PixelsPerInchX As Double
            Get
                Return _scaleX * 96R
            End Get
        End Property

        Public ReadOnly Property PixelsPerInchY As Double
            Get
                Return _scaleY * 96R
            End Get
        End Property
    End Class
End Class
