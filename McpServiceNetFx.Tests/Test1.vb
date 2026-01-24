Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports System.IO
Imports McpServiceNetFx

''' <summary>
''' PathHelper 和 PathPattern 类的单元测试
''' </summary>
<TestClass>
Public Class PathHelperTests

#Region "NormalizePath Tests"

    ''' <summary>
    ''' 测试 NormalizePath - 空字符串应返回空字符串
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_EmptyString_ReturnsEmptyString()
        ' Arrange
        Dim input As String = String.Empty

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.AreEqual(String.Empty, result)
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - null 应返回空字符串
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_Null_ReturnsEmptyString()
        ' Arrange
        Dim input As String = Nothing

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.AreEqual(String.Empty, result)
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - 仅空白字符应返回空字符串
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_Whitespace_ReturnsEmptyString()
        ' Arrange
        Dim input As String = "   "

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.AreEqual(String.Empty, result)
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - 波浪号 (~) 应转换为用户配置文件路径
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_TildeOnly_ReturnsUserProfilePath()
        ' Arrange
        Dim input As String = "~"
        Dim expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.AreEqual(expected, result)
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - 波浪号加路径 (~/Documents) 应正确转换
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_TildeWithPath_ReturnsUserProfileSubPath()
        ' Arrange
        Dim input As String = "~/Documents"
        Dim expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents")

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.AreEqual(expected, result, "~/Documents 应转换为用户配置文件下的 Documents 文件夹")
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - 波浪号加多级路径应正确转换
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_TildeWithNestedPath_ReturnsUserProfileNestedPath()
        ' Arrange
        Dim input As String = "~/Documents/Projects/Test"
        Dim expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Projects", "Test")

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.AreEqual(expected, result, "~/Documents/Projects/Test 应转换为用户配置文件下的嵌套路径")
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - POSIX 风格驱动器路径 (/d/projects) 应转换为 Windows 路径
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_PosixDrivePath_ConvertsToWindowsPath()
        ' Arrange
        Dim input As String = "/d/projects"

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.IsTrue(result.StartsWith("D:"), "POSIX 路径 /d/ 应转换为 D:")
        Assert.IsTrue(result.Contains("projects"), "应保留路径其余部分")
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - POSIX 风格大写驱动器路径 (/C/Windows) 应正确转换
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_PosixUppercaseDrivePath_ConvertsToWindowsPath()
        ' Arrange
        Dim input As String = "/C/Windows/System32"
        Dim expected = Path.GetFullPath("C:\Windows\System32")

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.AreEqual(expected, result)
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - 相对路径应转换为完整路径
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_RelativePath_ConvertsToFullPath()
        ' Arrange
        Dim input As String = "folder\subfolder"
        Dim expected = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "folder", "subfolder"))

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.AreEqual(expected, result)
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - 点相对路径 (.\folder) 应正确处理
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_DotRelativePath_ConvertsToFullPath()
        ' Arrange
        Dim input As String = ".\folder"
        Dim expected = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "folder"))

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.AreEqual(expected, result)
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - 双点相对路径 (..\folder) 应正确处理
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_DoubleDotRelativePath_ConvertsToFullPath()
        ' Arrange
        Dim input As String = "..\folder"

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.IsTrue(Path.IsPathRooted(result), "结果应为完整路径")
        Assert.IsTrue(result.EndsWith("folder"), "应保留目标文件夹")
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - 已存在的绝对路径应仅规范化
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_AbsolutePath_ReturnsNormalizedPath()
        ' Arrange
        Dim input As String = "C:\Projects\Test"
        Dim expected = Path.GetFullPath("C:\Projects\Test")

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.AreEqual(expected, result)
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - 混合分隔符路径应统一为系统分隔符
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_MixedSeparators_UnifiesToSystemSeparator()
        ' Arrange
        Dim input As String = "C:/Projects\Test\Folder/file.txt"

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.IsFalse(result.Contains("/"), "不应包含正斜杠")
        Assert.AreEqual(Path.DirectorySeparatorChar, result(2), "驱动器后应为系统分隔符")
    End Sub

    ''' <summary>
    ''' 测试 NormalizePath - POSIX 根路径 (/) 应处理
    ''' </summary>
    <TestMethod>
    Public Sub NormalizePath_PosixRootPath_HandlesGracefully()
        ' Arrange
        Dim input As String = "/"

        ' Act
        Dim result = PathHelper.NormalizePath(input)

        ' Assert
        Assert.IsNotNull(result)
    End Sub

#End Region

#Region "LikePath (String) Tests"

    ''' <summary>
    ''' 测试 LikePath - 空文件路径应返回 False
    ''' </summary>
    <TestMethod>
    Public Sub LikePath_EmptyFilePath_ReturnsFalse()
        ' Arrange
        Dim filePath As String = String.Empty
        Dim pattern As String = "*.txt"

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsFalse(result)
    End Sub

    ''' <summary>
    ''' 测试 LikePath - null 文件路径应返回 False
    ''' </summary>
    <TestMethod>
    Public Sub LikePath_NullFilePath_ReturnsFalse()
        ' Arrange
        Dim filePath As String = Nothing
        Dim pattern As String = "*.txt"

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsFalse(result)
    End Sub

    ''' <summary>
    ''' 测试 LikePath - 空模式应返回 False
    ''' </summary>
    <TestMethod>
    Public Sub LikePath_EmptyPattern_ReturnsFalse()
        ' Arrange
        Dim filePath As String = "C:\Test\file.txt"
        Dim pattern As String = String.Empty

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsFalse(result)
    End Sub

    ''' <summary>
    ''' 测试 LikePath - 星号通配符应匹配任意字符
    ''' </summary>
    <TestMethod>
    Public Sub LikePath_AsteriskWildcard_MatchesAnyCharacters()
        ' Arrange
        Dim filePath As String = "C:\Test\file.txt"
        Dim pattern As String = "C:\Test\*.txt"

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsTrue(result)
    End Sub

    ''' <summary>
    ''' 测试 LikePath - 问号通配符应匹配单个字符
    ''' </summary>
    <TestMethod>
    Public Sub LikePath_QuestionMarkWildcard_MatchesSingleCharacter()
        ' Arrange
        Dim filePath As String = "C:\Test\file.txt"
        Dim pattern As String = "C:\Test\fi??.txt"

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsTrue(result)
    End Sub

    ''' <summary>
    ''' 测试 LikePath - 路径分隔符应统一化
    ''' </summary>
    <TestMethod>
    Public Sub LikePath_DifferentPathSeparators_MatchesAfterNormalization()
        ' Arrange
        Dim filePath As String = "C:/Test/file.txt"
        Dim pattern As String = "C:\Test\*.txt"

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsTrue(result, "路径分隔符差异应被规范化")
    End Sub

    ''' <summary>
    ''' 测试 LikePath - 精确路径匹配
    ''' </summary>
    <TestMethod>
    Public Sub LikePath_ExactPath_Matches()
        ' Arrange
        Dim filePath As String = "C:\Test\file.txt"
        Dim pattern As String = "C:\Test\file.txt"

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsTrue(result)
    End Sub

    ''' <summary>
    ''' 测试 LikePath - 不匹配的路径应返回 False
    ''' </summary>
    <TestMethod>
    Public Sub LikePath_NonMatchingPath_ReturnsFalse()
        ' Arrange
        Dim filePath As String = "C:\Test\file.txt"
        Dim pattern As String = "C:\Other\*.txt"

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsFalse(result)
    End Sub

    ''' <summary>
    ''' 测试 LikePath - 波浪号路径应被正确处理
    ''' </summary>
    <TestMethod>
    Public Sub LikePath_TildePath_MatchesAfterNormalization()
        ' Arrange
        Dim filePath As String = "~/Documents/file.txt"
        Dim userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        Dim pattern As String = Path.Combine(userProfile, "Documents", "*.txt")

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsTrue(result, "波浪号路径应被正确标准化并匹配模式")
    End Sub

    ''' <summary>
    ''' 测试 LikePath - POSIX 路径应被正确处理
    ''' </summary>
    <TestMethod>
    Public Sub LikePath_PosixPath_MatchesAfterNormalization()
        ' Arrange
        Dim filePath As String = "/d/projects/file.txt"
        Dim pattern As String = "D:\projects\*.txt"

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsTrue(result, "POSIX 路径应被正确转换为 Windows 路径")
    End Sub

#End Region

#Region "LikePath (PathPattern) Tests"

    ''' <summary>
    ''' 测试 LikePath (PathPattern) - null 文件路径应返回 False
    ''' </summary>
    <TestMethod>
    Public Sub LikePathPathPattern_NullFilePath_ReturnsFalse()
        ' Arrange
        Dim filePath As String = Nothing
        Dim pattern As New PathPattern("*.txt")

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsFalse(result)
    End Sub

    ''' <summary>
    ''' 测试 LikePath (PathPattern) - null 模式应返回 False
    ''' </summary>
    <TestMethod>
    Public Sub LikePathPathPattern_NullPattern_ReturnsFalse()
        ' Arrange
        Dim filePath As String = "C:\Test\file.txt"
        Dim pattern As PathPattern = Nothing

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsFalse(result)
    End Sub

    ''' <summary>
    ''' 测试 LikePath (PathPattern) - 匹配包含模式应返回 True
    ''' </summary>
    <TestMethod>
    Public Sub LikePathPathPattern_MatchingInclude_ReturnsTrue()
        ' Arrange
        Dim filePath As String = "C:\Test\file.txt"
        Dim pattern As New PathPattern("*.txt")

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsTrue(result)
    End Sub

    ''' <summary>
    ''' 测试 LikePath (PathPattern) - 不匹配任何包含模式应返回 False
    ''' </summary>
    <TestMethod>
    Public Sub LikePathPathPattern_NoMatchingInclude_ReturnsFalse()
        ' Arrange
        Dim filePath As String = "C:\Test\file.txt"
        Dim pattern As New PathPattern("*.log")

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsFalse(result)
    End Sub

    ''' <summary>
    ''' 测试 LikePath (PathPattern) - 匹配包含但被排除应返回 False
    ''' </summary>
    <TestMethod>
    Public Sub LikePathPathPattern_IncludedThenExcluded_ReturnsFalse()
        ' Arrange
        Dim filePath As String = "C:\Test\file.txt"
        Dim pattern As New PathPattern("*.txt;!C:\Test\*")

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsFalse(result, "被排除模式匹配的文件应返回 False")
    End Sub

    ''' <summary>
    ''' 测试 LikePath (PathPattern) - 匹配包含且不被排除应返回 True
    ''' </summary>
    <TestMethod>
    Public Sub LikePathPathPattern_IncludedNotExcluded_ReturnsTrue()
        ' Arrange
        Dim filePath As String = "C:\Test\file.txt"
        Dim pattern As New PathPattern("*.txt;!C:\Other\*")

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsTrue(result, "未被排除的包含文件应返回 True")
    End Sub

    ''' <summary>
    ''' 测试 LikePath (PathPattern) - 多个包含模式之一匹配
    ''' </summary>
    <TestMethod>
    Public Sub LikePathPathPattern_MultipleIncludePatterns_MatchesAny()
        ' Arrange
        Dim filePath As String = "C:\Test\file.txt"
        Dim pattern As New PathPattern("*.log;*.txt;*.csv")

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsTrue(result)
    End Sub

    ''' <summary>
    ''' 测试 LikePath (PathPattern) - 多个排除模式任一匹配
    ''' </summary>
    <TestMethod>
    Public Sub LikePathPathPattern_MultipleExcludePatterns_ExcludedByAny()
        ' Arrange
        Dim filePath As String = "C:\Test\file.txt"
        Dim pattern As New PathPattern("*.txt;!C:\Test\*;!C:\Temp\*")

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsFalse(result, "任一排除模式匹配都应返回 False")
    End Sub

#End Region

#Region "PathPattern Constructor Tests"

    ''' <summary>
    ''' 测试 PathPattern 构造函数 - 空模式应抛出 ArgumentException
    ''' </summary>
    <TestMethod>
    Public Sub PathPatternConstructor_EmptyPattern_ThrowsArgumentException()
        ' Arrange
        Dim pattern As String = String.Empty
        Dim exceptionCaught As Boolean = False
        Dim caughtException As Exception = Nothing

        ' Act
        Try
            Dim pathPattern As New PathPattern(pattern)
        Catch ex As ArgumentException
            exceptionCaught = True
            caughtException = ex
        End Try

        ' Assert
        Assert.IsTrue(exceptionCaught, "空模式应抛出 ArgumentException")
        Assert.IsInstanceOfType(caughtException, GetType(ArgumentException))
    End Sub

    ''' <summary>
    ''' 测试 PathPattern 构造函数 - null 模式应抛出 ArgumentException
    ''' </summary>
    <TestMethod>
    Public Sub PathPatternConstructor_NullPattern_ThrowsArgumentException()
        ' Arrange
        Dim pattern As String = Nothing
        Dim exceptionCaught As Boolean = False
        Dim caughtException As Exception = Nothing

        ' Act
        Try
            Dim pathPattern As New PathPattern(pattern)
        Catch ex As ArgumentException
            exceptionCaught = True
            caughtException = ex
        End Try

        ' Assert
        Assert.IsTrue(exceptionCaught, "null 模式应抛出 ArgumentException")
        Assert.IsInstanceOfType(caughtException, GetType(ArgumentException))
    End Sub

    ''' <summary>
    ''' 测试 PathPattern 构造函数 - 仅空白模式应抛出 ArgumentException
    ''' </summary>
    <TestMethod>
    Public Sub PathPatternConstructor_WhitespacePattern_ThrowsArgumentException()
        ' Arrange
        Dim pattern As String = "   "
        Dim exceptionCaught As Boolean = False
        Dim caughtException As Exception = Nothing

        ' Act
        Try
            Dim pathPattern As New PathPattern(pattern)
        Catch ex As ArgumentException
            exceptionCaught = True
            caughtException = ex
        End Try

        ' Assert
        Assert.IsTrue(exceptionCaught, "仅空白模式应抛出 ArgumentException")
        Assert.IsInstanceOfType(caughtException, GetType(ArgumentException))
    End Sub

    ''' <summary>
    ''' 测试 PathPattern 构造函数 - 仅包含模式
    ''' </summary>
    <TestMethod>
    Public Sub PathPatternConstructor_IncludeOnly_ParsesCorrectly()
        ' Arrange
        Dim pattern As String = "*.txt;*.log"

        ' Act
        Dim pathPattern As New PathPattern(pattern)

        ' Assert
        Assert.AreEqual(2, pathPattern.IncludePatterns.Count)
        Assert.IsTrue(pathPattern.IncludePatterns.Contains("*.txt"))
        Assert.IsTrue(pathPattern.IncludePatterns.Contains("*.log"))
        Assert.AreEqual(0, pathPattern.ExcludePatterns.Count)
    End Sub

    ''' <summary>
    ''' 测试 PathPattern 构造函数 - 仅排除模式
    ''' </summary>
    <TestMethod>
    Public Sub PathPatternConstructor_ExcludeOnly_ParsesCorrectly()
        ' Arrange
        Dim pattern As String = "!*.tmp;!*.bak"

        ' Act
        Dim pathPattern As New PathPattern(pattern)

        ' Assert
        Assert.AreEqual(0, pathPattern.IncludePatterns.Count)
        Assert.AreEqual(2, pathPattern.ExcludePatterns.Count)
        Assert.IsTrue(pathPattern.ExcludePatterns.Contains("*.tmp"))
        Assert.IsTrue(pathPattern.ExcludePatterns.Contains("*.bak"))
    End Sub

    ''' <summary>
    ''' 测试 PathPattern 构造函数 - 混合包含和排除模式
    ''' </summary>
    <TestMethod>
    Public Sub PathPatternConstructor_MixedIncludeExclude_ParsesCorrectly()
        ' Arrange
        Dim pattern As String = "*.txt;*.log;!*.tmp;!.bak"

        ' Act
        Dim pathPattern As New PathPattern(pattern)

        ' Assert
        Assert.AreEqual(2, pathPattern.IncludePatterns.Count)
        Assert.AreEqual(2, pathPattern.ExcludePatterns.Count)
        Assert.IsTrue(pathPattern.IncludePatterns.Contains("*.txt"))
        Assert.IsTrue(pathPattern.ExcludePatterns.Contains("*.tmp"))
    End Sub

    ''' <summary>
    ''' 测试 PathPattern 构造函数 - 空模式应被忽略
    ''' </summary>
    <TestMethod>
    Public Sub PathPatternConstructor_EmptySectionsInPattern_IgnoresEmpties()
        ' Arrange
        Dim pattern As String = "*.txt;;*.log;"

        ' Act
        Dim pathPattern As New PathPattern(pattern)

        ' Assert
        Assert.AreEqual(2, pathPattern.IncludePatterns.Count)
    End Sub

    ''' <summary>
    ''' 测试 PathPattern 构造函数 - 模式前后空格应被修剪
    ''' </summary>
    <TestMethod>
    Public Sub PathPatternConstructor_WhitespaceInPattern_TrimsPatterns()
        ' Arrange
        Dim pattern As String = " *.txt ; !*.tmp "

        ' Act
        Dim pathPattern As New PathPattern(pattern)

        ' Assert
        Assert.AreEqual(1, pathPattern.IncludePatterns.Count)
        Assert.AreEqual(1, pathPattern.ExcludePatterns.Count)
        Assert.IsTrue(pathPattern.IncludePatterns.Contains("*.txt"))
        Assert.IsTrue(pathPattern.ExcludePatterns.Contains("*.tmp"))
    End Sub

    ''' <summary>
    ''' 测试 PathPattern 构造函数 - 排除模式去除感叹号
    ''' </summary>
    <TestMethod>
    Public Sub PathPatternConstructor_ExcludePattern_RemovesExclamationMark()
        ' Arrange
        Dim pattern As String = "!C:\Test\*.tmp"

        ' Act
        Dim pathPattern As New PathPattern(pattern)

        ' Assert
        Assert.AreEqual(1, pathPattern.ExcludePatterns.Count)
        Dim excludePattern = pathPattern.ExcludePatterns(0)
        Assert.IsFalse(excludePattern.StartsWith("!"), "排除模式应去除感叹号")
        Assert.AreEqual("C:\Test\*.tmp", excludePattern)
    End Sub

#End Region

#Region "PathPattern Properties Tests"

    ''' <summary>
    ''' 测试 IncludePatterns 属性 - 返回包含模式列表
    ''' </summary>
    <TestMethod>
    Public Sub IncludePatterns_ReturnsReadOnlyList()
        ' Arrange
        Dim pattern As New PathPattern("*.txt;*.log")
        Dim expectedType = GetType(IReadOnlyList(Of String))

        ' Act
        Dim result = pattern.IncludePatterns

        ' Assert
        Assert.IsNotNull(result)
        Assert.IsInstanceOfType(result, expectedType)
    End Sub

    ''' <summary>
    ''' 测试 ExcludePatterns 属性 - 返回排除模式列表
    ''' </summary>
    <TestMethod>
    Public Sub ExcludePatterns_ReturnsReadOnlyList()
        ' Arrange
        Dim pattern As New PathPattern("!*.tmp;!*.bak")
        Dim expectedType = GetType(IReadOnlyList(Of String))

        ' Act
        Dim result = pattern.ExcludePatterns

        ' Assert
        Assert.IsNotNull(result)
        Assert.IsInstanceOfType(result, expectedType)
    End Sub

#End Region

#Region "Integration Tests"

    ''' <summary>
    ''' 集成测试 - 完整的路径模式匹配流程
    ''' </summary>
    <TestMethod>
    Public Sub Integration_PathPatternMatching_EndToEnd()
        ' Arrange
        Dim filePath As String = "C:\Projects\Test\file.txt"
        Dim patternString As String = "C:\Projects\*.txt;!C:\Projects\Test\*"
        Dim pattern As New PathPattern(patternString)

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsFalse(result, "文件应匹配包含模式但被排除模式排除")
    End Sub

    ''' <summary>
    ''' 集成测试 - 波浪号路径与 PathPattern 结合
    ''' </summary>
    <TestMethod>
    Public Sub Integration_TildePathWithPattern_MatchesCorrectly()
        ' Arrange
        Dim filePath As String = "~/Documents/file.txt"
        Dim userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        Dim patternString As String = Path.Combine(userProfile, "Documents", "*.txt")
        Dim pattern As New PathPattern(patternString)

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsTrue(result, "波浪号路径应通过 PathPattern 匹配")
    End Sub

    ''' <summary>
    ''' 集成测试 - POSIX 路径与 PathPattern 结合
    ''' </summary>
    <TestMethod>
    Public Sub Integration_PosixPathWithPattern_MatchesCorrectly()
        ' Arrange
        Dim filePath As String = "/d/projects/file.txt"
        Dim pattern As New PathPattern("D:\projects\*.txt")

        ' Act
        Dim result = PathHelper.LikePath(filePath, pattern)

        ' Assert
        Assert.IsTrue(result, "POSIX 路径应通过模式匹配")
    End Sub

#End Region

End Class
