Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>
''' 文件操作辅助类
''' 提供安全的文件读写、HASH计算等核心功能
''' </summary>
Public NotInheritable Class FileOperationHelper
    ''' <summary>
    ''' 最大读取行数
    ''' </summary>
    Public Const MAX_READ_LINES As Integer = 100

    ''' <summary>
    ''' 最大读取字节数 (1MB)
    ''' </summary>
    Public Const MAX_READ_SIZE As Long = 1024 * 1024

    ''' <summary>
    ''' 最大写入字节数 (10MB)
    ''' </summary>
    Public Const MAX_WRITE_SIZE As Long = 10 * 1024 * 1024

    ''' <summary>
    ''' 私有构造函数，防止实例化
    ''' </summary>
    Private Sub New()
    End Sub

    ''' <summary>
    ''' 计算文件的SHA256 HASH值
    ''' </summary>
    ''' <param name="filePath">文件路径</param>
    ''' <returns>HASH值的十六进制字符串</returns>
    Public Shared Function CalculateFileHash(filePath As String) As String
        If Not File.Exists(filePath) Then
            Return String.Empty
        End If

        Try
            Using sha256Alg = SHA256.Create()
                Using stream = File.OpenRead(filePath)
                    Dim hashBytes = sha256Alg.ComputeHash(stream)
                    Return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant()
                End Using
            End Using
        Catch ex As Exception
            Return String.Empty
        End Try
    End Function

    ''' <summary>
    ''' 验证文件路径是否安全
    ''' </summary>
    ''' <param name="filePath">文件路径</param>
    ''' <returns>路径是否安全</returns>
    Public Shared Function ValidatePath(filePath As String) As Boolean
        If String.IsNullOrWhiteSpace(filePath) Then
            Return False
        End If

        Try
            Dim fullPath = Path.GetFullPath(filePath)

            ' 检查是否为绝对路径
            If Not Path.IsPathRooted(filePath) Then
                Return False
            End If

            ' 检查路径中是否包含危险字符
            Dim dangerousChars = {"..", "<", ">", "|", """", "*"}
            For Each dangerousChar In dangerousChars
                If fullPath.Contains(dangerousChar) Then
                    Return False
                End If
            Next

            ' 检查是否尝试访问系统目录
            Dim systemDirs = {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            }

            For Each sysDir In systemDirs
                If fullPath.StartsWith(sysDir, StringComparison.OrdinalIgnoreCase) Then
                    Return False
                End If
            Next

            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 安全地读取文件行
    ''' </summary>
    ''' <param name="filePath">文件路径</param>
    ''' <param name="start">起始行号（从0开始）</param>
    ''' <param name="length">读取行数</param>
    ''' <returns>读取结果</returns>
    Public Shared Function ReadFileLinesSafely(filePath As String, start As Integer, length As Integer) As FileReadResult
        Dim result As New FileReadResult With {
            .Success = False,
            .Lines = Array.Empty(Of String),
            .TotalLines = 0,
            .Hash = String.Empty
        }

        ' 参数验证
        If Not ValidatePath(filePath) Then
            result.Error = "文件路径无效或不安全"
            result.ErrorCode = "INVALID_PATH"
            Return result
        End If

        If Not File.Exists(filePath) Then
            result.Error = "文件不存在"
            result.ErrorCode = "FILE_NOT_FOUND"
            Return result
        End If

        If start < 0 Then
            result.Error = "起始行号不能为负数"
            result.ErrorCode = "INVALID_START_LINE"
            Return result
        End If

        If length <= 0 OrElse length > MAX_READ_LINES Then
            result.Error = $"读取行数必须在1到{MAX_READ_LINES}之间"
            result.ErrorCode = "INVALID_LENGTH"
            Return result
        End If

        Try
            ' 检查文件大小
            Dim fileInfo As New FileInfo(filePath)
            If fileInfo.Length > MAX_READ_SIZE Then
                result.Error = $"文件过大，超过{MAX_READ_SIZE}字节限制"
                result.ErrorCode = "SIZE_EXCEEDED"
                Return result
            End If

            ' 计算HASH值
            result.Hash = CalculateFileHash(filePath)

            ' 读取所有行
            Dim allLines = File.ReadAllLines(filePath, Encoding.UTF8)
            result.TotalLines = allLines.Length

            ' 计算实际读取范围
            If start >= result.TotalLines Then
                result.Success = True
                result.Lines = Array.Empty(Of String)
                result.Error = "起始行号超出文件范围"
                result.ErrorCode = "START_BEYOND_EOF"
                Return result
            End If

            Dim actualStart = start
            Dim actualLength = Math.Min(length, result.TotalLines - start)
            Dim actualEnd = actualStart + actualLength - 1

            ' 提取指定范围的行
            Dim selectedLines(actualLength - 1) As String
            Array.Copy(allLines, actualStart, selectedLines, 0, actualLength)

            result.Lines = selectedLines
            result.Success = True
            result.Error = String.Empty
            result.ErrorCode = String.Empty

        Catch ex As UnauthorizedAccessException
            result.Error = "访问被拒绝"
            result.ErrorCode = "ACCESS_DENIED"
        Catch ex As IOException
            result.Error = $"文件IO错误: {ex.Message}"
            result.ErrorCode = "FILE_LOCKED"
        Catch ex As Exception
            result.Error = $"读取文件时发生错误: {ex.Message}"
            result.ErrorCode = "READ_ERROR"
        End Try

        Return result
    End Function

    ''' <summary>
    ''' 安全地写入文件
    ''' </summary>
    ''' <param name="filePath">文件路径</param>
    ''' <param name="content">要写入的内容</param>
    ''' <param name="expectedHash">期望的HASH值（空字符串则跳过校验）</param>
    ''' <returns>写入结果</returns>
    Public Shared Function WriteFileSafely(filePath As String, content As String, expectedHash As String) As FileWriteResult
        Dim result As New FileWriteResult With {
            .Success = False,
            .LinesWritten = 0,
            .BytesWritten = 0,
            .NewHash = String.Empty
        }

        ' 参数验证
        If Not ValidatePath(filePath) Then
            result.Error = "文件路径无效或不安全"
            result.ErrorCode = "INVALID_PATH"
            Return result
        End If

        If content Is Nothing Then
            content = String.Empty
        End If

        Dim contentBytes = Encoding.UTF8.GetBytes(content)
        If contentBytes.Length > MAX_WRITE_SIZE Then
            result.Error = $"内容过大，超过{MAX_WRITE_SIZE}字节限制"
            result.ErrorCode = "SIZE_EXCEEDED"
            Return result
        End If

        Try
            ' 检查目录是否存在，不存在则创建
            Dim directoryPath = Path.GetDirectoryName(filePath)
            If Not String.IsNullOrWhiteSpace(directoryPath) AndAlso Not Directory.Exists(directoryPath) Then
                Directory.CreateDirectory(directoryPath)
            End If

            ' 如果文件存在，验证HASH值
            If File.Exists(filePath) AndAlso Not String.IsNullOrWhiteSpace(expectedHash) Then
                Dim currentHash = CalculateFileHash(filePath)
                If Not String.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase) Then
                    result.Error = "文件已被修改，请重新读取"
                    result.ErrorCode = "HASH_MISMATCH"
                    Return result
                End If
            End If

            ' 创建临时文件
            Dim tempPath = filePath & ".tmp." & Guid.NewGuid().ToString("N")

            ' 写入临时文件
            File.WriteAllText(tempPath, content, Encoding.UTF8)

            ' 验证临时文件写入成功
            If Not File.Exists(tempPath) Then
                result.Error = "创建临时文件失败"
                result.ErrorCode = "TEMP_FILE_ERROR"
                Return result
            End If

            ' 原子性替换原文件
            File.Delete(filePath) ' 如果原文件存在则删除
            File.Move(tempPath, filePath)

            ' 计算新的HASH值
            result.NewHash = CalculateFileHash(filePath)
            result.LinesWritten = content.Split({vbCrLf, vbLf}, StringSplitOptions.None).Length
            result.BytesWritten = contentBytes.Length
            result.Success = True

        Catch ex As UnauthorizedAccessException
            result.Error = "访问被拒绝"
            result.ErrorCode = "ACCESS_DENIED"
        Catch ex As IOException
            result.Error = $"文件IO错误: {ex.Message}"
            result.ErrorCode = "FILE_LOCKED"
        Catch ex As Exception
            result.Error = $"写入文件时发生错误: {ex.Message}"
            result.ErrorCode = "WRITE_ERROR"
        End Try

        Return result
    End Function

    ''' <summary>
    ''' 安全地追加内容到文件
    ''' </summary>
    ''' <param name="filePath">文件路径</param>
    ''' <param name="content">要追加的内容</param>
    ''' <returns>追加结果</returns>
    Public Shared Function AppendToFileSafely(filePath As String, content As String) As FileWriteResult
        Dim result As New FileWriteResult With {
            .Success = False,
            .LinesWritten = 0,
            .BytesWritten = 0,
            .NewHash = String.Empty
        }

        ' 参数验证
        If Not ValidatePath(filePath) Then
            result.Error = "文件路径无效或不安全"
            result.ErrorCode = "INVALID_PATH"
            Return result
        End If

        If content Is Nothing Then
            content = String.Empty
        End If

        ' 确保内容以换行符结束（如果不为空）
        If Not String.IsNullOrEmpty(content) AndAlso Not content.EndsWith(vbCrLf) AndAlso Not content.EndsWith(vbLf) Then
            content &= vbCrLf
        End If

        Dim contentBytes = Encoding.UTF8.GetBytes(content)
        If contentBytes.Length > MAX_WRITE_SIZE Then
            result.Error = $"内容过大，超过{MAX_WRITE_SIZE}字节限制"
            result.ErrorCode = "SIZE_EXCEEDED"
            Return result
        End If

        Try
            ' 检查目录是否存在，不存在则创建
            Dim directoryPath = Path.GetDirectoryName(filePath)
            If Not String.IsNullOrWhiteSpace(directoryPath) AndAlso Not Directory.Exists(directoryPath) Then
                Directory.CreateDirectory(directoryPath)
            End If

            ' 追加内容到文件
            File.AppendAllText(filePath, content, Encoding.UTF8)

            ' 计算新的HASH值和统计信息
            result.NewHash = CalculateFileHash(filePath)
            result.LinesWritten = content.Split({vbCrLf, vbLf}, StringSplitOptions.None).Length
            result.BytesWritten = contentBytes.Length
            result.Success = True

        Catch ex As UnauthorizedAccessException
            result.Error = "访问被拒绝"
            result.ErrorCode = "ACCESS_DENIED"
        Catch ex As IOException
            result.Error = $"文件IO错误: {ex.Message}"
            result.ErrorCode = "FILE_LOCKED"
        Catch ex As Exception
            result.Error = $"追加文件时发生错误: {ex.Message}"
            result.ErrorCode = "APPEND_ERROR"
        End Try

        Return result
    End Function

    ''' <summary>
    ''' 在文件中进行字符串替换
    ''' </summary>
    ''' <param name="filePath">文件路径</param>
    ''' <param name="oldText">要替换的文本</param>
    ''' <param name="newText">新文本</param>
    ''' <param name="options">替换选项</param>
    ''' <param name="expectedHash">期望的HASH值</param>
    ''' <returns>替换结果</returns>
    Public Shared Function ReplaceInFile(filePath As String, oldText As String, newText As String, options As StringReplaceOptions, expectedHash As String) As FileReplaceResult
        Dim result As New FileReplaceResult With {
            .Success = False,
            .ReplacementsCount = 0,
            .NewHash = String.Empty
        }

        ' 参数验证
        If Not ValidatePath(filePath) Then
            result.Error = "文件路径无效或不安全"
            result.ErrorCode = "INVALID_PATH"
            Return result
        End If

        If String.IsNullOrEmpty(oldText) Then
            result.Error = "要替换的文本不能为空"
            result.ErrorCode = "INVALID_OLD_TEXT"
            Return result
        End If

        If newText Is Nothing Then
            newText = String.Empty
        End If

        If Not File.Exists(filePath) Then
            result.Error = "文件不存在"
            result.ErrorCode = "FILE_NOT_FOUND"
            Return result
        End If

        Try
            ' 验证HASH值
            If Not String.IsNullOrWhiteSpace(expectedHash) Then
                Dim currentHash = CalculateFileHash(filePath)
                If Not String.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase) Then
                    result.Error = "文件已被修改，请重新读取"
                    result.ErrorCode = "HASH_MISMATCH"
                    Return result
                End If
            End If

            ' 读取文件内容
            Dim content = File.ReadAllText(filePath, Encoding.UTF8)
            Dim originalContent = content

            ' 执行替换
            If options.UseRegex Then
                ' 构建正则表达式选项
                Dim regexOptions As RegexOptions = RegexOptions.ECMAScript
                If options.IgnoreCase Then regexOptions = regexOptions Or RegexOptions.IgnoreCase
                If options.Multiline Then regexOptions = regexOptions Or RegexOptions.Multiline
                If options.Singleline Then regexOptions = regexOptions Or RegexOptions.Singleline

                Try
                    Dim regex As New Regex(oldText, regexOptions)
                    content = regex.Replace(content, newText)
                    result.ReplacementsCount = Regex.Matches(originalContent, oldText, regexOptions).Count
                Catch ex As ArgumentException
                    result.Error = $"正则表达式语法错误: {ex.Message}"
                    result.ErrorCode = "REGEX_ERROR"
                    Return result
                End Try
            Else
                ' 普通字符串替换
                Dim comparison As StringComparison
                If options.IgnoreCase Then
                    comparison = StringComparison.OrdinalIgnoreCase
                Else
                    comparison = StringComparison.Ordinal
                End If

                ' 计算替换次数
                result.ReplacementsCount = CountOccurrences(originalContent, oldText, comparison)

                ' 执行替换
                content = ReplaceWithComparison(originalContent, oldText, newText, comparison)
            End If

            ' 检查是否有实际更改
            If String.Equals(content, originalContent, StringComparison.Ordinal) Then
                result.Success = True
                result.NewHash = CalculateFileHash(filePath)
                result.Error = "没有找到匹配的文本"
                result.ErrorCode = "NO_MATCHES"
                Return result
            End If

            ' 写入新内容
            Dim writeResult = WriteFileSafely(filePath, content, expectedHash)
            If writeResult.Success Then
                result.Success = True
                result.NewHash = writeResult.NewHash
            Else
                result.Error = writeResult.Error
                result.ErrorCode = writeResult.ErrorCode
            End If

        Catch ex As Exception
            result.Error = $"替换文件内容时发生错误: {ex.Message}"
            result.ErrorCode = "REPLACE_ERROR"
        End Try

        Return result
    End Function

    ''' <summary>
    ''' 替换文件中指定范围的行
    ''' </summary>
    ''' <param name="filePath">文件路径</param>
    ''' <param name="start">起始行号（从0开始）</param>
    ''' <param name="length">要替换的行数</param>
    ''' <param name="newContent">新内容</param>
    ''' <param name="expectedHash">期望的HASH值</param>
    ''' <returns>替换结果</returns>
    Public Shared Function ReplaceLinesInFile(filePath As String, start As Integer, length As Integer, newContent As String, expectedHash As String) As FileReplaceResult
        Dim result As New FileReplaceResult With {
            .Success = False,
            .ReplacementsCount = 0,
            .NewHash = String.Empty
        }

        ' 参数验证
        If Not ValidatePath(filePath) Then
            result.Error = "文件路径无效或不安全"
            result.ErrorCode = "INVALID_PATH"
            Return result
        End If

        If start < 0 Then
            result.Error = "起始行号不能为负数"
            result.ErrorCode = "INVALID_START_LINE"
            Return result
        End If

        If length < 0 Then
            result.Error = "替换行数不能为负数"
            result.ErrorCode = "INVALID_LENGTH"
            Return result
        End If

        If newContent Is Nothing Then
            newContent = String.Empty
        End If

        If Not File.Exists(filePath) Then
            result.Error = "文件不存在"
            result.ErrorCode = "FILE_NOT_FOUND"
            Return result
        End If

        Try
            ' 验证HASH值
            If Not String.IsNullOrWhiteSpace(expectedHash) Then
                Dim currentHash = CalculateFileHash(filePath)
                If Not String.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase) Then
                    result.Error = "文件已被修改，请重新读取"
                    result.ErrorCode = "HASH_MISMATCH"
                    Return result
                End If
            End If

            ' 读取文件所有行
            Dim allLines = File.ReadAllLines(filePath, Encoding.UTF8)
            Dim totalLines = allLines.Length

            ' 验证行号范围
            If start > totalLines Then
                result.Error = "起始行号超出文件范围"
                result.ErrorCode = "START_BEYOND_EOF"
                Return result
            End If

        Dim actualLength As Integer
            If start = totalLines Then
                ' 在文件末尾插入或追加
                actualLength = 0
            Else
                actualLength = Math.Min(length, totalLines - start)
            End If

            Dim endLine = start + actualLength - 1

            ' 分割新内容为行
            Dim newLines = newContent.Split({vbCrLf, vbLf}, StringSplitOptions.None)

            ' 构建新的行数组
            Dim newFileLines(totalLines - actualLength + newLines.Length - 1) As String

            ' 复制替换前的行
            If start > 0 Then
                Array.Copy(allLines, 0, newFileLines, 0, start)
            End If

            ' 插入新行
            Array.Copy(newLines, 0, newFileLines, start, newLines.Length)

            ' 复制替换后的行（只在actualLength > 0时执行）
            If actualLength > 0 AndAlso endLine + 1 < totalLines Then
                Array.Copy(allLines, endLine + 1, newFileLines, start + newLines.Length, totalLines - endLine - 1)
            ElseIf actualLength = 0 AndAlso start < totalLines Then
                ' 如果是插入操作（length=0），复制起始位置之后的所有行
                Array.Copy(allLines, start, newFileLines, start + newLines.Length, totalLines - start)
            End If

            ' 写入新内容
            Dim newFileContent = String.Join(vbCrLf, newFileLines)
            Dim writeResult = WriteFileSafely(filePath, newFileContent, expectedHash)

            If writeResult.Success Then
                result.Success = True
                result.NewHash = writeResult.NewHash
                result.ReplacementsCount = actualLength
            Else
                result.Error = writeResult.Error
                result.ErrorCode = writeResult.ErrorCode
            End If

        Catch ex As Exception
            result.Error = $"替换文件行时发生错误: {ex.Message}"
            result.ErrorCode = "REPLACE_LINES_ERROR"
        End Try

        Return result
    End Function

    ''' <summary>
    ''' 计算字符串中指定文本的出现次数
    ''' </summary>
    ''' <param name="source">源字符串</param>
    ''' <param name="target">目标字符串</param>
    ''' <param name="comparison">比较方式</param>
    ''' <returns>出现次数</returns>
    Private Shared Function CountOccurrences(source As String, target As String, comparison As StringComparison) As Integer
        If String.IsNullOrEmpty(source) OrElse String.IsNullOrEmpty(target) Then
            Return 0
        End If

        Dim count = 0
        Dim index = 0

        While True
            index = source.IndexOf(target, index, comparison)
            If index = -1 Then Exit While
            count += 1
            index += target.Length
        End While

        Return count
    End Function

    ''' <summary>
    ''' 使用指定比较方式替换字符串
    ''' </summary>
    ''' <param name="source">源字符串</param>
    ''' <param name="oldText">旧文本</param>
    ''' <param name="newText">新文本</param>
    ''' <param name="comparison">比较方式</param>
    ''' <returns>替换后的字符串</returns>
    Private Shared Function ReplaceWithComparison(source As String, oldText As String, newText As String, comparison As StringComparison) As String
        If comparison = StringComparison.OrdinalIgnoreCase Then
            ' 大小写不敏感的替换
            Dim regexOptions As RegexOptions = RegexOptions.IgnoreCase
            Dim regex As New Regex(Regex.Escape(oldText), regexOptions)
            Return regex.Replace(source, newText)
        Else
            ' 大小写敏感的替换
            Return source.Replace(oldText, newText)
        End If
    End Function
End Class