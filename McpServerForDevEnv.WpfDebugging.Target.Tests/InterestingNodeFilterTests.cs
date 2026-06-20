using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using McpServerForDevEnv.WpfDebugging;
using McpServerForDevEnv.WpfDebugging.Target;

namespace McpServerForDevEnv.WpfDebugging.Target.Tests;

// InterestingNodeFilter 的纯逻辑测试：裁掉纯装饰节点，interesting 子孙提升到父位置。
// 纯内存对象，无 WPF / 无 pipe / 无文件，无副作用。

[TestClass]
public class InterestingNodeFilterTests
{
    [TestMethod]
    public void IsInteresting_NameSet_IsInteresting()
    {
        var n = new SnapshotNode { Uid = "1", TypeName = "Border", Name = "MyBorder" };
        Assert.IsTrue(InterestingNodeFilter.IsInteresting(n));
    }

    [TestMethod]
    public void IsInteresting_InteractiveType_IsInteresting()
    {
        Assert.IsTrue(InterestingNodeFilter.IsInteresting(new SnapshotNode { Uid = "1", TypeName = "Button" }));
        Assert.IsTrue(InterestingNodeFilter.IsInteresting(new SnapshotNode { Uid = "1", TypeName = "TextBox" }));
        Assert.IsTrue(InterestingNodeFilter.IsInteresting(new SnapshotNode { Uid = "1", TypeName = "ComboBox" }));
        Assert.IsTrue(InterestingNodeFilter.IsInteresting(new SnapshotNode { Uid = "1", TypeName = "CheckBox" }));
    }

    [TestMethod]
    public void IsInteresting_PlainLayoutType_NotInteresting()
    {
        Assert.IsFalse(InterestingNodeFilter.IsInteresting(new SnapshotNode { Uid = "1", TypeName = "Border" }));
        Assert.IsFalse(InterestingNodeFilter.IsInteresting(new SnapshotNode { Uid = "1", TypeName = "Grid" }));
        Assert.IsFalse(InterestingNodeFilter.IsInteresting(new SnapshotNode { Uid = "1", TypeName = "StackPanel" }));
    }

    [TestMethod]
    public void IsInteresting_SignalPropertyTrue_IsInteresting()
    {
        var n = new SnapshotNode
        {
            Uid = "1",
            TypeName = "ContentControl",
            Properties = new Dictionary<string, string> { ["IsFocused"] = "True" }
        };
        Assert.IsTrue(InterestingNodeFilter.IsInteresting(n));
    }

    [TestMethod]
    public void IsInteresting_SignalPropertyFalse_NotInteresting()
    {
        var n = new SnapshotNode
        {
            Uid = "1",
            TypeName = "ContentControl",
            Properties = new Dictionary<string, string> { ["IsFocused"] = "False" }
        };
        Assert.IsFalse(InterestingNodeFilter.IsInteresting(n));
    }

    [TestMethod]
    public void Filter_UninterestingLeaf_Dropped_RootKept()
    {
        // root(Grid) 下挂一个纯装饰 Border，无 interesting 子 → Border 被剪，root 保留（无子）。
        var root = new SnapshotNode
        {
            Uid = "1", TypeName = "Grid",
            Children = new List<SnapshotNode>
            {
                new() { Uid = "2", TypeName = "Border" }
            }
        };
        var filtered = InterestingNodeFilter.Filter(root);

        Assert.IsNotNull(filtered);
        Assert.AreEqual("1", filtered.Uid);          // 根保留
        Assert.IsNull(filtered.Children);            // Border 被剪
    }

    [TestMethod]
    public void Filter_UninterestingNode_PromotesInterestingGrandchild()
    {
        // root → Grid(不 interesting) → Button(interesting)
        // Grid 应折叠，Button 提升为 root 的直接子。
        var root = new SnapshotNode
        {
            Uid = "1", TypeName = "Window",
            Children = new List<SnapshotNode>
            {
                new()
                {
                    Uid = "2", TypeName = "Grid",
                    Children = new List<SnapshotNode>
                    {
                        new() { Uid = "3", TypeName = "Button" }
                    }
                }
            }
        };
        var filtered = InterestingNodeFilter.Filter(root);

        Assert.IsNotNull(filtered);
        Assert.AreEqual("1", filtered.Uid);
        Assert.IsNotNull(filtered.Children);
        Assert.AreEqual(1, filtered.Children.Count);
        Assert.AreEqual("3", filtered.Children[0].Uid);   // Button 直接挂到 Window 下
        Assert.AreEqual("Button", filtered.Children[0].TypeName);
    }

    [TestMethod]
    public void Filter_UninterestingNode_PromotesMultipleGrandchildren()
    {
        // root → Border(不 interesting) → [Button, TextBox]
        // Border 折叠，两个孙都提升。
        var root = new SnapshotNode
        {
            Uid = "1", TypeName = "Window",
            Children = new List<SnapshotNode>
            {
                new()
                {
                    Uid = "2", TypeName = "Border",
                    Children = new List<SnapshotNode>
                    {
                        new() { Uid = "3", TypeName = "Button" },
                        new() { Uid = "4", TypeName = "TextBox" }
                    }
                }
            }
        };
        var filtered = InterestingNodeFilter.Filter(root);

        Assert.IsNotNull(filtered.Children);
        Assert.AreEqual(2, filtered.Children.Count);
        Assert.AreEqual("3", filtered.Children[0].Uid);
        Assert.AreEqual("4", filtered.Children[1].Uid);
    }

    [TestMethod]
    public void Filter_KeepsInterestingSubtreeIntact()
    {
        // root → Button(interesting) → [TextBlock(不 interesting, 无名), Border(不 interesting)]
        // Button 保留，其无 interesting 子全剪掉。
        var root = new SnapshotNode
        {
            Uid = "1", TypeName = "Window",
            Children = new List<SnapshotNode>
            {
                new()
                {
                    Uid = "2", TypeName = "Button",
                    Children = new List<SnapshotNode>
                    {
                        new() { Uid = "3", TypeName = "TextBlock" },
                        new() { Uid = "4", TypeName = "Border" }
                    }
                }
            }
        };
        var filtered = InterestingNodeFilter.Filter(root);

        Assert.IsNotNull(filtered.Children);
        Assert.AreEqual(1, filtered.Children.Count);
        var btn = filtered.Children[0];
        Assert.AreEqual("Button", btn.TypeName);
        Assert.IsNull(btn.Children);    // Button 的装饰子全被剪
    }

    [TestMethod]
    public void Filter_DoesNotMutateInput()
    {
        var root = new SnapshotNode
        {
            Uid = "1", TypeName = "Grid",
            Children = new List<SnapshotNode> { new() { Uid = "2", TypeName = "Border" } }
        };
        InterestingNodeFilter.Filter(root);

        // 原树未被修改。
        Assert.IsNotNull(root.Children);
        Assert.AreEqual(1, root.Children.Count);
        Assert.AreEqual("Border", root.Children[0].TypeName);
    }
}
