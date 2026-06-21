Imports System.Collections.Generic
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServerForDevEnv.WpfDebugging
Imports McpServerForDevEnv.WpfDebugging.Target

' SnapshotFormatter 的纯逻辑测试：把假 SnapshotNode 树渲染成 a11y 风格文本。
' 纯内存对象，无 WPF / 无 pipe / 无文件，无副作用。

<TestClass>
Public Class SnapshotFormatterTests

    <TestMethod>
    Public Sub Format_RootNode_HasUidAndTypeName()
        Dim root As New SnapshotNode With {.Uid = "42", .TypeName = "Window"}
        Dim text As String = SnapshotFormatter.Format(root)

        ' 第一行：uid=42 Window
        Assert.IsTrue(text.StartsWith("uid=42 Window"))
    End Sub

    <TestMethod>
    Public Sub Format_NameQuoted()
        Dim root As New SnapshotNode With {.Uid = "1", .TypeName = "Button", .Name = "OK"}
        Dim text As String = SnapshotFormatter.Format(root)

        Assert.IsTrue(text.Contains("Button ""OK"""))
    End Sub

    <TestMethod>
    Public Sub Format_BoolTrue_PrintsKeyOnly()
        Dim root As New SnapshotNode With {
            .Uid = "1",
            .TypeName = "Button",
            .Properties = New Dictionary(Of String, String) From {{"IsEnabled", "True"}}
        }
        Dim text As String = SnapshotFormatter.Format(root)

        Assert.IsTrue(text.Contains(" IsEnabled"))
        Assert.IsFalse(text.Contains("IsEnabled="))
    End Sub

    <TestMethod>
    Public Sub Format_BoolFalse_Omitted()
        Dim root As New SnapshotNode With {
            .Uid = "1",
            .TypeName = "Button",
            .Properties = New Dictionary(Of String, String) From {{"IsEnabled", "False"}, {"IsFocused", "True"}}
        }
        Dim text As String = SnapshotFormatter.Format(root)

        Assert.IsFalse(text.Contains("IsEnabled"))
        Assert.IsTrue(text.Contains("IsFocused"))
    End Sub

    <TestMethod>
    Public Sub Format_ScalarValue_PrintsKeyEqualsQuotedValue()
        Dim root As New SnapshotNode With {
            .Uid = "1",
            .TypeName = "TextBox",
            .Properties = New Dictionary(Of String, String) From {{"Visibility", "Collapsed"}, {"ActualSize", "100x30"}}
        }
        Dim text As String = SnapshotFormatter.Format(root)

        Assert.IsTrue(text.Contains("Visibility=""Collapsed"""))
        Assert.IsTrue(text.Contains("ActualSize=""100x30"""))
    End Sub

    <TestMethod>
    Public Sub Format_ChildrenIndentedByDepth()
        Dim root As New SnapshotNode With {
            .Uid = "1",
            .TypeName = "Window",
            .Children = New List(Of SnapshotNode) From {
                New SnapshotNode With {
                    .Uid = "2",
                    .TypeName = "Grid",
                    .Children = New List(Of SnapshotNode) From {
                        New SnapshotNode With {.Uid = "3", .TypeName = "Button"}
                    }
                }
            }
        }
        Dim text As String = SnapshotFormatter.Format(root)
        Dim lines = text.Split(ControlChars.Lf)

        ' 行0: root 无缩进；行1: Grid 缩进2；行2: Button 缩进4（末尾空行由 AppendLine 产生）
        Assert.IsTrue(lines(0).StartsWith("uid=1 Window"))
        Assert.IsTrue(lines(1).StartsWith("  uid=2 Grid"))
        Assert.IsTrue(lines(2).StartsWith("    uid=3 Button"))
    End Sub

    <TestMethod>
    Public Sub Format_NullRoot_ReturnsEmpty()
        Assert.AreEqual(String.Empty, SnapshotFormatter.Format(Nothing))
    End Sub

    <TestMethod>
    Public Sub Format_NewlinesInNameEscaped()
        Dim root As New SnapshotNode With {.Uid = "1", .TypeName = "TextBlock", .Name = "a" & ControlChars.Lf & "b"}
        Dim text As String = SnapshotFormatter.Format(root)
        Dim firstLine = text.Split(ControlChars.Lf)(0)

        ' 名称里的换行被转义为 \n 字面量，渲染成一行内容 + 末尾 AppendLine 产生的空串 = 2 段。
        Assert.AreEqual(2, text.Split(ControlChars.Lf).Length)
        Assert.IsTrue(firstLine.Contains("a\nb"))
    End Sub

    <TestMethod>
    Public Sub Format_ValueWithBackslash_EscapesBackslash()
        Dim root As New SnapshotNode With {
            .Uid = "1",
            .TypeName = "TextBlock",
            .Properties = New Dictionary(Of String, String) From {{"Path", "C:\temp"}}
        }
        Dim text As String = SnapshotFormatter.Format(root)

        ' 反斜杠转义为 \\，保持单行结构。
        Assert.AreEqual(2, text.Split(ControlChars.Lf).Length)
        Assert.IsTrue(text.Contains("Path=""C:\\temp"""))
    End Sub

End Class
