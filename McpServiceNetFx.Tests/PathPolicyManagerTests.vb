Imports Microsoft.VisualStudio.TestTools.UnitTesting

<TestClass>
Public Class PathPolicyManagerTests
    <TestMethod>
    Sub Constructor_InitializesEmptyCollections()
        Dim manager = New PathPolicyManager()
        Assert.IsEmpty(manager.AllowPolicies)
        Assert.IsEmpty(manager.DenyPolicies)
    End Sub

    <TestMethod>
    Sub AddPolicy_Allow_AddsToAllowPolicies()
        Dim manager = New PathPolicyManager()
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "*.txt")
        Assert.HasCount(1, manager.AllowPolicies)
        Assert.IsEmpty(manager.DenyPolicies)
    End Sub

    <TestMethod>
    Sub AddPolicy_Deny_AddsToDenyPolicies()
        Dim manager = New PathPolicyManager()
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.Write, "*.log")
        Assert.IsEmpty(manager.AllowPolicies)
        Assert.HasCount(1, manager.DenyPolicies)
    End Sub

    <TestMethod>
    Sub AddPolicy_Duplicate_DoesNotAdd()
        Dim manager = New PathPolicyManager()
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "*.txt")
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "*.txt")
        Assert.HasCount(1, manager.AllowPolicies)
    End Sub

    <TestMethod>
    Sub RemovePolicy_RemovesFromCollection()
        Dim manager = New PathPolicyManager()
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "*.txt")
        Dim policy = manager.AllowPolicies(0)
        manager.RemovePolicy(policy)
        Assert.IsEmpty(manager.AllowPolicies)
    End Sub

    <TestMethod>
    Sub ClearPolicies_ClearsAllCollections()
        Dim manager = New PathPolicyManager()
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "*.txt")
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.Write, "*.log")
        manager.ClearPolicies()
        Assert.IsEmpty(manager.AllowPolicies)
        Assert.IsEmpty(manager.DenyPolicies)
    End Sub

    <TestMethod>
    Sub CheckPolicies_DenyTakesPriority()
        Dim manager = New PathPolicyManager()
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "C:\Test\*")
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.Read, "C:\Test\secret.txt")
        Dim result = manager.CheckPolicies("C:\Test\secret.txt", FileAccessType.Read)
        Assert.IsTrue(result.Matched)
        Assert.IsFalse(result.IsAllowed)
    End Sub

    <TestMethod>
    Sub CheckPolicies_AllowMatch_ReturnsAllow()
        Dim manager = New PathPolicyManager()
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "*.txt")
        Dim result = manager.CheckPolicies("C:\Test\file.txt", FileAccessType.Read)
        Assert.IsTrue(result.Matched)
        Assert.IsTrue(result.IsAllowed)
    End Sub

    <TestMethod>
    Sub CheckPolicies_NoMatch_ReturnsNoMatch()
        Dim manager = New PathPolicyManager()
        Dim result = manager.CheckPolicies("C:\Test\file.txt", FileAccessType.Read)
        Assert.IsFalse(result.Matched)
        Assert.IsNull(result.IsAllowed)
    End Sub

    <TestMethod>
    Sub CheckPolicies_FileTypeCheck_ReadVsWrite()
        Dim manager = New PathPolicyManager()
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "*.txt")
        Dim result = manager.CheckPolicies("C:\Test\file.txt", FileAccessType.Write)
        Assert.IsFalse(result.Matched)
    End Sub

    <TestMethod>
    Sub MultipleAllowPolicies_FirstMatchWins()
        Dim manager = New PathPolicyManager()
        ' 添加多个允许策略，按添加顺序
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "*.txt")
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "C:\Test\*")
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "*")

        ' 文件匹配所有三个策略，但应该返回第一个匹配的
        Dim result = manager.CheckPolicies("C:\Test\file.txt", FileAccessType.Read)
        Assert.IsTrue(result.Matched)
        Assert.IsTrue(result.IsAllowed)
        ' 验证返回的是第一个匹配的 *.txt 策略
        Assert.AreEqual("*.txt", result.MatchedPolicy.Pattern)
    End Sub

    <TestMethod>
    Sub DenyOverridesWildcardAllow_ConcretePath()
        Dim manager = New PathPolicyManager()
        ' 通配符允许
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "C:\Test\*")
        ' 具体路径拒绝（更具体的规则）
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.Read, "C:\Test\secret.txt")

        ' 通配符允许的文件应该被允许
        Dim allowResult = manager.CheckPolicies("C:\Test\public.txt", FileAccessType.Read)
        Assert.IsTrue(allowResult.Matched)
        Assert.IsTrue(allowResult.IsAllowed)

        ' 具体拒绝的文件应该被拒绝（拒绝优先）
        Dim denyResult = manager.CheckPolicies("C:\Test\secret.txt", FileAccessType.Read)
        Assert.IsTrue(denyResult.Matched)
        Assert.IsFalse(denyResult.IsAllowed)
    End Sub

    <TestMethod>
    Sub MixedReadWritePolicies_EachTypeIndependent()
        Dim manager = New PathPolicyManager()
        ' 允许读取 *.txt
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "*.txt")
        ' 拒绝写入 *.txt
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.Write, "*.txt")

        Dim filePath = "C:\Test\file.txt"

        ' 读取应该被允许
        Dim readResult = manager.CheckPolicies(filePath, FileAccessType.Read)
        Assert.IsTrue(readResult.Matched)
        Assert.IsTrue(readResult.IsAllowed)

        ' 写入应该被拒绝
        Dim writeResult = manager.CheckPolicies(filePath, FileAccessType.Write)
        Assert.IsTrue(writeResult.Matched)
        Assert.IsFalse(writeResult.IsAllowed)

        ' 读写操作应该被拒绝（因为写入被拒绝）
        Dim readWriteResult = manager.CheckPolicies(filePath, FileAccessType.ReadWrite)
        Assert.IsTrue(readWriteResult.Matched)
        Assert.IsFalse(readWriteResult.IsAllowed)
    End Sub

    <TestMethod>
    Sub ReadWriteDeny_BlocksAllAccessTypes()
        Dim manager = New PathPolicyManager()
        ' ReadWrite 拒绝策略适用于所有访问类型
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.ReadWrite, "*.txt")

        Dim filePath = "C:\Test\file.txt"

        ' Read 请求应该被拒绝
        Dim readResult = manager.CheckPolicies(filePath, FileAccessType.Read)
        Assert.IsTrue(readResult.Matched)
        Assert.IsFalse(readResult.IsAllowed)

        ' Write 请求应该被拒绝
        Dim writeResult = manager.CheckPolicies(filePath, FileAccessType.Write)
        Assert.IsTrue(writeResult.Matched)
        Assert.IsFalse(writeResult.IsAllowed)

        ' ReadWrite 请求应该被拒绝
        Dim readWriteResult = manager.CheckPolicies(filePath, FileAccessType.ReadWrite)
        Assert.IsTrue(readWriteResult.Matched)
        Assert.IsFalse(readWriteResult.IsAllowed)
    End Sub

    <TestMethod>
    Sub ReadDeny_OnlyBlocksRead_NotWrite()
        Dim manager = New PathPolicyManager()
        ' Read 拒绝策略只影响 Read 请求
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.Read, "*.txt")

        Dim filePath = "C:\Test\file.txt"

        ' Read 请求应该被拒绝
        Dim readResult = manager.CheckPolicies(filePath, FileAccessType.Read)
        Assert.IsTrue(readResult.Matched)
        Assert.IsFalse(readResult.IsAllowed)

        ' Write 请求不应该受影响（无匹配策略）
        Dim writeResult = manager.CheckPolicies(filePath, FileAccessType.Write)
        Assert.IsFalse(writeResult.Matched)

        ' ReadWrite 请求应该被 Read 拒绝策略匹配（因为 ReadWrite 包含 Read）
        ' 没有写权限就不能打开文件进行读写
        Dim readWriteResult = manager.CheckPolicies(filePath, FileAccessType.ReadWrite)
        Assert.IsTrue(readWriteResult.Matched)
        Assert.IsFalse(readWriteResult.IsAllowed)
    End Sub

    <TestMethod>
    Sub WriteDeny_OnlyBlocksWrite_NotRead()
        Dim manager = New PathPolicyManager()
        ' Write 拒绝策略只影响 Write 请求
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.Write, "*.txt")

        Dim filePath = "C:\Test\file.txt"

        ' Read 请求不应该受影响（无匹配策略）
        Dim readResult = manager.CheckPolicies(filePath, FileAccessType.Read)
        Assert.IsFalse(readResult.Matched)

        ' Write 请求应该被拒绝
        Dim writeResult = manager.CheckPolicies(filePath, FileAccessType.Write)
        Assert.IsTrue(writeResult.Matched)
        Assert.IsFalse(writeResult.IsAllowed)

        ' ReadWrite 请求应该被 Write 拒绝策略匹配（因为 ReadWrite 包含 Write）
        ' 没有写权限就不能打开文件进行读写
        Dim readWriteResult = manager.CheckPolicies(filePath, FileAccessType.ReadWrite)
        Assert.IsTrue(readWriteResult.Matched)
        Assert.IsFalse(readWriteResult.IsAllowed)
    End Sub

    <TestMethod>
    Sub MultipleDenyPolicies_FirstDenyMatchWins()
        Dim manager = New PathPolicyManager()
        ' 添加多个拒绝策略
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.Read, "C:\Test\*.txt")
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.Write, "*.txt")
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.ReadWrite, "C:\Test\*")

        ' 匹配第一个拒绝策略
        Dim result1 = manager.CheckPolicies("C:\Test\file.txt", FileAccessType.Read)
        Assert.IsTrue(result1.Matched)
        Assert.IsFalse(result1.IsAllowed)
        Assert.AreEqual("C:\Test\*.txt", result1.MatchedPolicy.Pattern)

        ' 匹配第二个拒绝策略
        Dim result2 = manager.CheckPolicies("C:\Test\file.txt", FileAccessType.Write)
        Assert.IsTrue(result2.Matched)
        Assert.IsFalse(result2.IsAllowed)
        Assert.AreEqual("*.txt", result2.MatchedPolicy.Pattern)
    End Sub

    <TestMethod>
    Sub ComplexAllowDenyCombination_RealWorldScenario()
        Dim manager = New PathPolicyManager()
        ' 真实场景：允许读取日志，但禁止写入敏感目录
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.Read, "C:\Logs\*")
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.Write, "C:\Logs\secret\*")
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.ReadWrite, "C:\Logs\public\*")

        ' 可以读取普通日志
        Dim result1 = manager.CheckPolicies("C:\Logs\app.log", FileAccessType.Read)
        Assert.IsTrue(result1.Matched)
        Assert.IsTrue(result1.IsAllowed)

        ' 不能写入秘密目录
        Dim result2 = manager.CheckPolicies("C:\Logs\secret\data.log", FileAccessType.Write)
        Assert.IsTrue(result2.Matched)
        Assert.IsFalse(result2.IsAllowed)

        ' 可以读写公共目录
        Dim result3 = manager.CheckPolicies("C:\Logs\public\info.txt", FileAccessType.ReadWrite)
        Assert.IsTrue(result3.Matched)
        Assert.IsTrue(result3.IsAllowed)
    End Sub

    <TestMethod>
    Sub ReadWriteAllow_AllowsReadAndWriteRequests()
        Dim manager = New PathPolicyManager()
        ' ReadWrite 允许策略适用于所有访问类型
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.ReadWrite, "*.txt")

        Dim filePath = "C:\Test\file.txt"

        ' Read 请求应该被允许
        Dim readResult = manager.CheckPolicies(filePath, FileAccessType.Read)
        Assert.IsTrue(readResult.Matched)
        Assert.IsTrue(readResult.IsAllowed)

        ' Write 请求应该被允许
        Dim writeResult = manager.CheckPolicies(filePath, FileAccessType.Write)
        Assert.IsTrue(writeResult.Matched)
        Assert.IsTrue(writeResult.IsAllowed)

        ' ReadWrite 请求应该被允许
        Dim readWriteResult = manager.CheckPolicies(filePath, FileAccessType.ReadWrite)
        Assert.IsTrue(readWriteResult.Matched)
        Assert.IsTrue(readWriteResult.IsAllowed)
    End Sub

    <TestMethod>
    Sub SpecificDenyOverridesGeneralAllow_DifferentAccessTypes()
        Dim manager = New PathPolicyManager()
        ' 通配允许读写
        manager.AddPolicy(PathPolicyType.Allow, FileAccessType.ReadWrite, "C:\Test\*")
        ' 具体拒绝读取
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.Read, "C:\Test\secret.txt")
        ' 具体拒绝写入
        manager.AddPolicy(PathPolicyType.Deny, FileAccessType.Write, "C:\Test\readonly.txt")

        ' secret.txt 不能读取但可以写入
        Dim result1 = manager.CheckPolicies("C:\Test\secret.txt", FileAccessType.Read)
        Assert.IsTrue(result1.Matched)
        Assert.IsFalse(result1.IsAllowed)

        Dim result2 = manager.CheckPolicies("C:\Test\secret.txt", FileAccessType.Write)
        Assert.IsTrue(result2.Matched)
        Assert.IsTrue(result2.IsAllowed)

        ' readonly.txt 不能写入但可以读取
        Dim result3 = manager.CheckPolicies("C:\Test\readonly.txt", FileAccessType.Write)
        Assert.IsTrue(result3.Matched)
        Assert.IsFalse(result3.IsAllowed)

        Dim result4 = manager.CheckPolicies("C:\Test\readonly.txt", FileAccessType.Read)
        Assert.IsTrue(result4.Matched)
        Assert.IsTrue(result4.IsAllowed)
    End Sub
End Class
