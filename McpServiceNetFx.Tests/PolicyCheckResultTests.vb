Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports McpServiceNetFx

<TestClass>
Public Class PolicyCheckResultTests
    <TestMethod>
    Sub NoMatch_HasCorrectProperties()
        Dim result = PolicyCheckResult.NoMatch
        Assert.IsFalse(result.Matched)
        Assert.IsNull(result.IsAllowed)
        Assert.IsNull(result.MatchedPolicy)
    End Sub

    <TestMethod>
    Sub Allow_HasCorrectProperties()
        Dim policy = New PathPermissionPolicy(PathPolicyType.Allow, FileAccessType.Read, "*.txt")
        Dim result = PolicyCheckResult.Allow(policy)
        Assert.IsTrue(result.Matched)
        Assert.IsTrue(result.IsAllowed)
        Assert.AreSame(policy, result.MatchedPolicy)
    End Sub

    <TestMethod>
    Sub Deny_HasCorrectProperties()
        Dim policy = New PathPermissionPolicy(PathPolicyType.Deny, FileAccessType.Write, "*.log")
        Dim result = PolicyCheckResult.Deny(policy)
        Assert.IsTrue(result.Matched)
        Assert.IsFalse(result.IsAllowed)
        Assert.AreSame(policy, result.MatchedPolicy)
    End Sub
End Class
