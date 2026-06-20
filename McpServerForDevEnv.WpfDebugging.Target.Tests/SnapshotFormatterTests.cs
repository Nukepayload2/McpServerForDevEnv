using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using McpServerForDevEnv.WpfDebugging;
using McpServerForDevEnv.WpfDebugging.Target;

namespace McpServerForDevEnv.WpfDebugging.Target.Tests;

// SnapshotFormatter 的纯逻辑测试：把假 SnapshotNode 树渲染成 a11y 风格文本。
// 纯内存对象，无 WPF / 无 pipe / 无文件，无副作用。

[TestClass]
public class SnapshotFormatterTests
{
    [TestMethod]
    public void Format_RootNode_HasUidAndTypeName()
    {
        var root = new SnapshotNode { Uid = "42", TypeName = "Window" };
        string text = SnapshotFormatter.Format(root);

        // 第一行：uid=42 Window
        Assert.IsTrue(text.StartsWith("uid=42 Window"));
    }

    [TestMethod]
    public void Format_NameQuoted()
    {
        var root = new SnapshotNode { Uid = "1", TypeName = "Button", Name = "OK" };
        string text = SnapshotFormatter.Format(root);

        Assert.IsTrue(text.Contains("Button \"OK\""));
    }

    [TestMethod]
    public void Format_BoolTrue_PrintsKeyOnly()
    {
        var root = new SnapshotNode
        {
            Uid = "1",
            TypeName = "Button",
            Properties = new Dictionary<string, string> { ["IsEnabled"] = "True" }
        };
        string text = SnapshotFormatter.Format(root);

        Assert.IsTrue(text.Contains(" IsEnabled"));
        Assert.IsFalse(text.Contains("IsEnabled="));
    }

    [TestMethod]
    public void Format_BoolFalse_Omitted()
    {
        var root = new SnapshotNode
        {
            Uid = "1",
            TypeName = "Button",
            Properties = new Dictionary<string, string> { ["IsEnabled"] = "False", ["IsFocused"] = "True" }
        };
        string text = SnapshotFormatter.Format(root);

        Assert.IsFalse(text.Contains("IsEnabled"));
        Assert.IsTrue(text.Contains("IsFocused"));
    }

    [TestMethod]
    public void Format_ScalarValue_PrintsKeyEqualsQuotedValue()
    {
        var root = new SnapshotNode
        {
            Uid = "1",
            TypeName = "TextBox",
            Properties = new Dictionary<string, string> { ["Visibility"] = "Collapsed", ["ActualSize"] = "100x30" }
        };
        string text = SnapshotFormatter.Format(root);

        Assert.IsTrue(text.Contains("Visibility=\"Collapsed\""));
        Assert.IsTrue(text.Contains("ActualSize=\"100x30\""));
    }

    [TestMethod]
    public void Format_ChildrenIndentedByDepth()
    {
        var root = new SnapshotNode
        {
            Uid = "1",
            TypeName = "Window",
            Children = new List<SnapshotNode>
            {
                new() { Uid = "2", TypeName = "Grid", Children = new List<SnapshotNode>
                {
                    new() { Uid = "3", TypeName = "Button" }
                }}
            }
        };
        string text = SnapshotFormatter.Format(root);
        var lines = text.Split('\n');

        // 行0: root 无缩进；行1: Grid 缩进2；行2: Button 缩进4（末尾空行由 AppendLine 产生）
        Assert.IsTrue(lines[0].StartsWith("uid=1 Window"));
        Assert.IsTrue(lines[1].StartsWith("  uid=2 Grid"));
        Assert.IsTrue(lines[2].StartsWith("    uid=3 Button"));
    }

    [TestMethod]
    public void Format_NullRoot_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, SnapshotFormatter.Format(null));
    }

    [TestMethod]
    public void Format_NewlinesInNameEscaped()
    {
        var root = new SnapshotNode { Uid = "1", TypeName = "TextBlock", Name = "a\nb" };
        string text = SnapshotFormatter.Format(root);
        var firstLine = text.Split('\n')[0];

        // 名称里的换行被转义为 \n 字面量，渲染成一行内容 + 末尾 AppendLine 产生的空串 = 2 段。
        Assert.AreEqual(2, text.Split('\n').Length);
        Assert.IsTrue(firstLine.Contains("a\\nb"));
    }

    [TestMethod]
    public void Format_ValueWithBackslash_EscapesBackslash()
    {
        var root = new SnapshotNode
        {
            Uid = "1",
            TypeName = "TextBlock",
            Properties = new Dictionary<string, string> { ["Path"] = @"C:\temp" }
        };
        string text = SnapshotFormatter.Format(root);

        // 反斜杠转义为 \\，保持单行结构。
        Assert.AreEqual(2, text.Split('\n').Length);
        Assert.IsTrue(text.Contains("Path=\"C:\\\\temp\""));
    }
}
