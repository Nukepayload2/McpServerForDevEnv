using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpServiceNet.Tools;

[McpServerToolType]
public sealed class FileSystemTools
{
    [McpServerTool, Description("Get a tree-structured list of files and directories in a given path.")]
    public static string GetFileList(
        [Description("The directory path to scan.")] string directoryPath,
        [Description("Maximum depth to scan recursively. Default is 3.")] int maxDepth = 3,
        [Description("Include files in the result. Default is true.")] bool includeFiles = true,
        [Description("Include directories in the result. Default is true.")] bool includeDirectories = true)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                throw new McpException($"Directory does not exist: {directoryPath}", McpErrorCode.InvalidParams);
            }

            if (maxDepth < 0)
            {
                throw new McpException("Max depth must be non-negative", McpErrorCode.InvalidParams);
            }

            var fileInfo = GetDirectoryInfo(directoryPath, maxDepth, includeFiles, includeDirectories, 0);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(fileInfo, options);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new McpException($"Access denied to path: {directoryPath}. {ex.Message}", McpErrorCode.InvalidParams);
        }
        catch (Exception ex) when (!(ex is McpException))
        {
            throw new McpException($"Error scanning directory: {ex.Message}", McpErrorCode.InternalError);
        }
    }

    private static FileSystemInfo GetDirectoryInfo(string path, int maxDepth, bool includeFiles, bool includeDirectories, int currentDepth)
    {
        var dirInfo = new DirectoryInfo(path);
        var result = new FileSystemInfo
        {
            Name = dirInfo.Name,
            Type = "directory",
            Path = path,
            Children = []
        };

        if (currentDepth >= maxDepth)
        {
            return result;
        }

        try
        {
            var entries = dirInfo.GetFileSystemInfos();

            foreach (var entry in entries.OrderBy(e => e is FileInfo ? 1 : 0).ThenBy(e => e.Name))
            {
                if (entry is FileInfo file && includeFiles)
                {
                    result.Children.Add(new FileSystemInfo
                    {
                        Name = file.Name,
                        Type = "file",
                        Path = file.FullName,
                        Size = file.Length
                    });
                }
                else if (entry is DirectoryInfo subDir && includeDirectories)
                {
                    result.Children.Add(GetDirectoryInfo(subDir.FullName, maxDepth, includeFiles, includeDirectories, currentDepth + 1));
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }

        return result;
    }
}

public class FileSystemInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long? Size { get; set; }
    public List<FileSystemInfo>? Children { get; set; }
}