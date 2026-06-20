Imports System.Collections.Generic
Imports Newtonsoft.Json
Imports McpServerForDevEnv.WpfDebugging

' SnapshotNode 树 / WindowInfo / ScreenshotResult 的序列化往返测试。纯内存，无副作用。

<TestClass>
Public Class SnapshotNodeTests

    <TestMethod>
    Public Sub SnapshotNode_Tree_Roundtrips_Through_Json()
        Dim root As New SnapshotNode With {
            .Uid = "1",
            .TypeName = "Window",
            .Name = "MainWindow",
            .Properties = New Dictionary(Of String, String) From {
                {"IsEnabled", "True"},
                {"Visibility", "Visible"}
            },
            .Children = New List(Of SnapshotNode) From {
                New SnapshotNode With {.Uid = "2", .TypeName = "Button", .Name = "OK"},
                New SnapshotNode With {
                    .Uid = "3",
                    .TypeName = "Grid",
                    .Children = New List(Of SnapshotNode) From {
                        New SnapshotNode With {.Uid = "4", .TypeName = "TextBox"}
                    }
                }
            }
        }

        Dim json As String = JsonConvert.SerializeObject(root)
        Dim back As SnapshotNode = JsonConvert.DeserializeObject(Of SnapshotNode)(json)

        Assert.AreEqual("1", back.Uid)
        Assert.AreEqual("Window", back.TypeName)
        Assert.AreEqual("MainWindow", back.Name)
        Assert.AreEqual("True", back.Properties("IsEnabled"))
        Assert.AreEqual(2, back.Children.Count)
        Assert.AreEqual("Button", back.Children(0).TypeName)
        Assert.AreEqual("Grid", back.Children(1).TypeName)
        Assert.AreEqual(1, back.Children(1).Children.Count)
        Assert.AreEqual("4", back.Children(1).Children(0).Uid)
    End Sub

    <TestMethod>
    Public Sub SnapshotNode_Leaf_Without_Optional_Collections_Roundtrips()
        Dim leaf As New SnapshotNode With {.Uid = "10", .TypeName = "TextBlock"}
        Dim json As String = JsonConvert.SerializeObject(leaf)
        Dim back As SnapshotNode = JsonConvert.DeserializeObject(Of SnapshotNode)(json)

        Assert.AreEqual("10", back.Uid)
        Assert.AreEqual("TextBlock", back.TypeName)
        Assert.IsNull(back.Children)
        Assert.IsNull(back.Properties)
    End Sub

    <TestMethod>
    Public Sub WindowInfo_Roundtrips_Through_Json()
        Dim info As New WindowInfo With {
            .WindowId = "w-1",
            .Title = "关于",
            .IsMain = True,
            .Handle = 123456L
        }
        Dim json As String = JsonConvert.SerializeObject(info)
        Dim back As WindowInfo = JsonConvert.DeserializeObject(Of WindowInfo)(json)

        Assert.AreEqual("w-1", back.WindowId)
        Assert.AreEqual("关于", back.Title)
        Assert.AreEqual(True, back.IsMain)
        Assert.AreEqual(123456L, back.Handle)
    End Sub

    <TestMethod>
    Public Sub ScreenshotResult_Roundtrips_Through_Json()
        Dim shot As New ScreenshotResult With {
            .Width = 800,
            .Height = 600,
            .PngBase64 = "BASE64DATA"
        }
        Dim json As String = JsonConvert.SerializeObject(shot)
        Dim back As ScreenshotResult = JsonConvert.DeserializeObject(Of ScreenshotResult)(json)

        Assert.AreEqual(800, back.Width)
        Assert.AreEqual(600, back.Height)
        Assert.AreEqual("BASE64DATA", back.PngBase64)
    End Sub

End Class
