using ModelContextProtocol;

namespace Daenet.McpFileSystemServer;

public sealed class PathSandbox
{
    private readonly string _rootDirectory;

    public PathSandbox(FileSystemServerOptions options)
    {
        _rootDirectory = NormalizePath(options.RootDirectory);

        if (!Directory.Exists(_rootDirectory))
        {
            Directory.CreateDirectory(_rootDirectory);
        }
    }

    public string RootDirectory => _rootDirectory;

    public string ResolvePath(string userPath)
    {
        if (string.IsNullOrWhiteSpace(userPath))
        {
            throw new McpProtocolException("Path is required.", McpErrorCode.InvalidParams);
        }

        string fullPath = Path.IsPathRooted(userPath)
            ? NormalizePath(userPath)
            : NormalizePath(Path.Combine(_rootDirectory, userPath));

        if (!IsPathUnderRoot(fullPath))
        {
            throw new McpProtocolException("Path is outside of the configured sandbox root.", McpErrorCode.InvalidParams);
        }

        return fullPath;
    }

    public string ToRelativePath(string fullPath)
    {
        string relative = Path.GetRelativePath(_rootDirectory, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string NormalizePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private bool IsPathUnderRoot(string fullPath)
    {
        if (string.Equals(fullPath, _rootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string rootWithSeparator = _rootDirectory + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
