Option Compare Text

Imports System.IO

''' <summary>
''' 路径辅助模块
''' 提供路径标准化和通配符匹配功能
''' </summary>
Public Module PathHelper
    ''' <summary>
    ''' 标准化路径
    ''' 处理用户目录 (~)、POSIX 路径，转换为完整路径
    ''' </summary>
    ''' <param name="path">要标准化的路径</param>
    ''' <returns>标准化后的完整路径</returns>
    Public Function NormalizePath(path As String) As String
        If String.IsNullOrWhiteSpace(path) Then
            Return String.Empty
        End If

        ' 处理用户目录 (~)
        If path.StartsWith("~") Then
            ' 移除 ~ 和可能紧跟的路径分隔符
            Dim remainingPath = path.Substring(1)
            If remainingPath.StartsWith("/") OrElse remainingPath.StartsWith("\") Then
                remainingPath = remainingPath.Substring(1)
            End If
            If String.IsNullOrEmpty(remainingPath) Then
                path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            Else
                path = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), remainingPath)
            End If
        End If

        ' 替换路径分隔符
        path = path.Replace("/"c, IO.Path.DirectorySeparatorChar).Replace("\"c, IO.Path.DirectorySeparatorChar)

        ' 处理 POSIX 风格的驱动器路径 (/d/projects -> D:\projects)
        If path.Length >= 3 AndAlso path(0) = IO.Path.DirectorySeparatorChar AndAlso Char.IsLetter(path(1)) AndAlso path(2) = IO.Path.DirectorySeparatorChar Then
            Dim driveLetter = Char.ToUpper(path(1))
            path = driveLetter & ":" & path.Substring(2)
        End If

        ' 转换为完整路径
        Try
            If IO.Path.IsPathRooted(path) Then
                ' 已是绝对路径，仅规范化
                path = IO.Path.GetFullPath(path)
            Else
                ' 相对路径，基于当前目录转换为完整路径
                path = IO.Path.GetFullPath(IO.Path.Combine(Environment.CurrentDirectory, path))
            End If
        Catch ex As ArgumentException
            ' 如果路径无效，返回原始路径（已做分隔符处理）
        Catch ex As PathTooLongException
            ' 路径过长，返回原始路径
        End Try

        Return path
    End Function

    ''' <summary>
    ''' 使用 Like 运算符进行路径通配符匹配
    ''' </summary>
    ''' <param name="filePath">要匹配的文件路径</param>
    ''' <param name="pathPattern">通配符模式</param>
    ''' <returns>如果路径匹配模式则返回 True</returns>
    Public Function LikePath(filePath As String, pathPattern As String) As Boolean
        If String.IsNullOrWhiteSpace(filePath) OrElse String.IsNullOrWhiteSpace(pathPattern) Then
            Return False
        End If

        ' 标准化两个路径
        Dim normalizedPath = NormalizePath(filePath)
        Dim normalizedPattern = NormalizePath(pathPattern)

        ' 使用 Like 运算符进行匹配
        Return normalizedPath Like normalizedPattern
    End Function

    ''' <summary>
    ''' 使用 Like 运算符进行路径通配符匹配（PathPattern 对象版本）
    ''' </summary>
    ''' <param name="filePath">要匹配的文件路径</param>
    ''' <param name="pathPattern">PathPattern 对象</param>
    ''' <returns>如果路径匹配模式则返回 True</returns>
    Public Function LikePath(filePath As String, pathPattern As PathPattern) As Boolean
        If String.IsNullOrWhiteSpace(filePath) OrElse pathPattern Is Nothing Then
            Return False
        End If

        ' 检查是否匹配包含模式
        Dim isIncluded As Boolean = False
        For Each includePattern In pathPattern.IncludePatterns
            If LikePath(filePath, includePattern) Then
                isIncluded = True
                Exit For
            End If
        Next

        ' 如果已包含，检查是否被排除
        If isIncluded Then
            Dim isExcluded As Boolean = False
            For Each excludePattern In pathPattern.ExcludePatterns
                If LikePath(filePath, excludePattern) Then
                    isExcluded = True
                    Exit For
                End If
            Next

            Return Not isExcluded
        End If

        Return False
    End Function
End Module

''' <summary>
''' 路径模式类
''' 解析分号分隔的模式字符串，支持 ! 前缀排除模式
''' </summary>
Public Class PathPattern
    Private ReadOnly _includePatterns As New List(Of String)
    Private ReadOnly _excludePatterns As New List(Of String)

    ''' <summary>
    ''' 创建路径模式
    ''' </summary>
    ''' <param name="patterns">分号分隔的模式字符串，以 ! 开头的模式为排除模式</param>
    Public Sub New(patterns As String)
        If String.IsNullOrWhiteSpace(patterns) Then
            Throw New ArgumentException("Patterns cannot be empty", NameOf(patterns))
        End If

        ' 分割模式并分类
        For Each pattern In patterns.Split(";"c)
            Dim trimmedPattern = pattern.Trim()
            If Not String.IsNullOrEmpty(trimmedPattern) Then
                If trimmedPattern.StartsWith("!"c) Then
                    _excludePatterns.Add(trimmedPattern.Substring(1))
                Else
                    _includePatterns.Add(trimmedPattern)
                End If
            End If
        Next
    End Sub

    ''' <summary>
    ''' 获取包含模式列表
    ''' </summary>
    Public ReadOnly Property IncludePatterns As IReadOnlyList(Of String)
        Get
            Return _includePatterns
        End Get
    End Property

    ''' <summary>
    ''' 获取排除模式列表
    ''' </summary>
    Public ReadOnly Property ExcludePatterns As IReadOnlyList(Of String)
        Get
            Return _excludePatterns
        End Get
    End Property
End Class
