using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.IO.Enumeration;
using System.Text;

namespace Daenet.McpFileSystemServer;

[McpServerToolType]
public sealed class FileSystemTools
{
    private readonly PathSandbox _pathSandbox;
    private readonly FileSystemServerOptions _options;
    private readonly Microsoft.Extensions.Logging.ILogger<FileSystemTools> _logger;

    public FileSystemTools(
        PathSandbox pathSandbox,
        FileSystemServerOptions options,
        Microsoft.Extensions.Logging.ILogger<FileSystemTools> logger)
    {
        _pathSandbox = pathSandbox;
        _options = options;
        _logger = logger;
    }

    [McpServerTool(Name = "read_file", ReadOnly = true, Idempotent = true)]
    [Description("Read text content from a file path in the sandbox.")]
    public async Task<string> ReadFile(
        [Description("File path relative to sandbox root or absolute path under root.")] string path,
        [Description("Optional text encoding name, for example utf-8 or utf-16.")] string? encoding = null,
        CancellationToken cancellationToken = default)
    {
        string resolvedPath = _pathSandbox.ResolvePath(path);
        Encoding selectedEncoding = ResolveEncoding(encoding);

        try
        {
            EnsureFileExists(resolvedPath);
            string content = await File.ReadAllTextAsync(resolvedPath, selectedEncoding, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("read_file succeeded: {Path}", resolvedPath);
            return content;
        }
        catch (Exception ex)
        {
            throw ToMcpException(ex, resolvedPath);
        }
    }

    [McpServerTool(Name = "write_file", Destructive = true)]
    [Description("Write text content to a file and overwrite if it exists.")]
    public async Task WriteFile(
        [Description("Target file path.")] string path,
        [Description("Content to write to file.")] string content,
        [Description("Optional text encoding name, for example utf-8 or utf-16.")] string? encoding = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable("write_file");

        string resolvedPath = _pathSandbox.ResolvePath(path);
        Encoding selectedEncoding = ResolveEncoding(encoding);

        try
        {
            string? directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(resolvedPath, content, selectedEncoding, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("write_file succeeded: {Path}", resolvedPath);
        }
        catch (Exception ex)
        {
            throw ToMcpException(ex, resolvedPath);
        }
    }

    [McpServerTool(Name = "append_file", Destructive = true)]
    [Description("Append text content to a file.")]
    public async Task AppendFile(
        [Description("Target file path.")] string path,
        [Description("Content to append.")] string content,
        [Description("Optional text encoding name, for example utf-8 or utf-16.")] string? encoding = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable("append_file");

        string resolvedPath = _pathSandbox.ResolvePath(path);
        Encoding selectedEncoding = ResolveEncoding(encoding);

        try
        {
            string? directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.AppendAllTextAsync(resolvedPath, content, selectedEncoding, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("append_file succeeded: {Path}", resolvedPath);
        }
        catch (Exception ex)
        {
            throw ToMcpException(ex, resolvedPath);
        }
    }

    [McpServerTool(Name = "list_directory", ReadOnly = true, Idempotent = true)]
    [Description("List files and subdirectories for a directory path.")]
    public async Task<IReadOnlyList<DirectoryEntryDto>> ListDirectory(
        [Description("Directory path to list.")] string path,
        CancellationToken cancellationToken = default)
    {
        string resolvedPath = _pathSandbox.ResolvePath(path);

        try
        {
            EnsureDirectoryExists(resolvedPath);

            var entries = await Task.Run(() =>
            {
                return Directory.EnumerateFileSystemEntries(resolvedPath)
                    .Select(entryPath =>
                    {
                        bool isDirectory = Directory.Exists(entryPath);
                        return new DirectoryEntryDto(
                            _pathSandbox.ToRelativePath(entryPath),
                            Path.GetFileName(entryPath),
                            isDirectory ? "directory" : "file");
                    })
                    .OrderBy(item => item.EntryType)
                    .ThenBy(item => item.Name)
                    .ToList();
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("list_directory succeeded: {Path}, Count={Count}", resolvedPath, entries.Count);
            return entries;
        }
        catch (Exception ex)
        {
            throw ToMcpException(ex, resolvedPath);
        }
    }

    [McpServerTool(Name = "create_directory", Destructive = true)]
    [Description("Create a directory path.")]
    public async Task CreateDirectory(
        [Description("Directory path to create.")] string path,
        [Description("If true, create missing parent directories recursively.")] bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable("create_directory");

        string resolvedPath = _pathSandbox.ResolvePath(path);

        try
        {
            await Task.Run(() =>
            {
                if (!recursive)
                {
                    string? parent = Path.GetDirectoryName(resolvedPath);
                    if (!string.IsNullOrWhiteSpace(parent) && !Directory.Exists(parent))
                    {
                        throw new McpProtocolException("Parent directory does not exist.", McpErrorCode.ResourceNotFound);
                    }
                }

                Directory.CreateDirectory(resolvedPath);
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("create_directory succeeded: {Path}", resolvedPath);
        }
        catch (Exception ex)
        {
            throw ToMcpException(ex, resolvedPath);
        }
    }

    [McpServerTool(Name = "delete_file", Destructive = true)]
    [Description("Delete a file.")]
    public async Task DeleteFile(
        [Description("File path to delete.")] string path,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable("delete_file");

        string resolvedPath = _pathSandbox.ResolvePath(path);

        try
        {
            EnsureFileExists(resolvedPath);
            await Task.Run(() => File.Delete(resolvedPath), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("delete_file succeeded: {Path}", resolvedPath);
        }
        catch (Exception ex)
        {
            throw ToMcpException(ex, resolvedPath);
        }
    }

    [McpServerTool(Name = "delete_directory", Destructive = true)]
    [Description("Delete a directory.")]
    public async Task DeleteDirectory(
        [Description("Directory path to delete.")] string path,
        [Description("If true, delete recursively.")] bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable("delete_directory");

        string resolvedPath = _pathSandbox.ResolvePath(path);

        try
        {
            EnsureDirectoryExists(resolvedPath);
            await Task.Run(() => Directory.Delete(resolvedPath, recursive), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("delete_directory succeeded: {Path}, Recursive={Recursive}", resolvedPath, recursive);
        }
        catch (Exception ex)
        {
            throw ToMcpException(ex, resolvedPath);
        }
    }

    [McpServerTool(Name = "move_file", Destructive = true)]
    [Description("Move or rename a file or directory.")]
    public async Task MoveFile(
        [Description("Source file or directory path.")] string sourcePath,
        [Description("Destination file or directory path.")] string destinationPath,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable("move_file");

        string resolvedSourcePath = _pathSandbox.ResolvePath(sourcePath);
        string resolvedDestinationPath = _pathSandbox.ResolvePath(destinationPath);

        try
        {
            await Task.Run(() =>
            {
                if (File.Exists(resolvedSourcePath))
                {
                    string? parent = Path.GetDirectoryName(resolvedDestinationPath);
                    if (!string.IsNullOrWhiteSpace(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    File.Move(resolvedSourcePath, resolvedDestinationPath, overwrite: true);
                    return;
                }

                if (Directory.Exists(resolvedSourcePath))
                {
                    if (Directory.Exists(resolvedDestinationPath))
                    {
                        throw new McpProtocolException("Destination directory already exists.", McpErrorCode.InvalidRequest);
                    }

                    Directory.Move(resolvedSourcePath, resolvedDestinationPath);
                    return;
                }

                throw new FileNotFoundException("Source path does not exist.", resolvedSourcePath);
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("move_file succeeded: {SourcePath} -> {DestinationPath}", resolvedSourcePath, resolvedDestinationPath);
        }
        catch (Exception ex)
        {
            throw ToMcpException(ex, resolvedSourcePath);
        }
    }

    [McpServerTool(Name = "copy_file", Destructive = true)]
    [Description("Copy a file to another location.")]
    public async Task CopyFile(
        [Description("Source file path.")] string sourcePath,
        [Description("Destination file path.")] string destinationPath,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable("copy_file");

        string resolvedSourcePath = _pathSandbox.ResolvePath(sourcePath);
        string resolvedDestinationPath = _pathSandbox.ResolvePath(destinationPath);

        try
        {
            EnsureFileExists(resolvedSourcePath);

            string? parent = Path.GetDirectoryName(resolvedDestinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            await using FileStream sourceStream = new FileStream(
                resolvedSourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous);

            await using FileStream destinationStream = new FileStream(
                resolvedDestinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous);

            await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("copy_file succeeded: {SourcePath} -> {DestinationPath}", resolvedSourcePath, resolvedDestinationPath);
        }
        catch (Exception ex)
        {
            throw ToMcpException(ex, resolvedSourcePath);
        }
    }

    [McpServerTool(Name = "get_file_info", ReadOnly = true, Idempotent = true)]
    [Description("Get metadata for a file or directory.")]
    public async Task<FileSystemInfoDto> GetFileInfo(
        [Description("File or directory path.")] string path,
        CancellationToken cancellationToken = default)
    {
        string resolvedPath = _pathSandbox.ResolvePath(path);

        try
        {
            FileSystemInfoDto info = await Task.Run(() =>
            {
                if (File.Exists(resolvedPath))
                {
                    var fileInfo = new FileInfo(resolvedPath);
                    return new FileSystemInfoDto(
                        _pathSandbox.ToRelativePath(resolvedPath),
                        "file",
                        fileInfo.Length,
                        fileInfo.CreationTimeUtc,
                        fileInfo.LastWriteTimeUtc,
                        fileInfo.Attributes.ToString());
                }

                if (Directory.Exists(resolvedPath))
                {
                    var directoryInfo = new DirectoryInfo(resolvedPath);
                    return new FileSystemInfoDto(
                        _pathSandbox.ToRelativePath(resolvedPath),
                        "directory",
                        null,
                        directoryInfo.CreationTimeUtc,
                        directoryInfo.LastWriteTimeUtc,
                        directoryInfo.Attributes.ToString());
                }

                throw new FileNotFoundException("Path does not exist.", resolvedPath);
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("get_file_info succeeded: {Path}", resolvedPath);
            return info;
        }
        catch (Exception ex)
        {
            throw ToMcpException(ex, resolvedPath);
        }
    }

    [McpServerTool(Name = "search_files", ReadOnly = true, Idempotent = true)]
    [Description("Search files by wildcard pattern under a directory tree.")]
    public async Task<IReadOnlyList<string>> SearchFiles(
        [Description("Root directory to search.")] string path,
        [Description("Wildcard file name pattern. Example: *.json")] string pattern = "*",
        CancellationToken cancellationToken = default)
    {
        string resolvedPath = _pathSandbox.ResolvePath(path);

        try
        {
            EnsureDirectoryExists(resolvedPath);
            var matches = await Task.Run(() =>
            {
                return Directory.EnumerateFiles(resolvedPath, "*", SearchOption.AllDirectories)
                    .Where(filePath => FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(filePath), ignoreCase: true))
                    .Select(_pathSandbox.ToRelativePath)
                    .OrderBy(static path => path)
                    .ToList();
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("search_files succeeded: {Path}, Pattern={Pattern}, Count={Count}", resolvedPath, pattern, matches.Count);
            return matches;
        }
        catch (Exception ex)
        {
            throw ToMcpException(ex, resolvedPath);
        }
    }

    private void EnsureWritable(string toolName)
    {
        if (_options.ReadOnlyMode)
        {
            _logger.LogWarning("{ToolName} blocked because server is in read-only mode.", toolName);
            throw new McpProtocolException("Server is configured in read-only mode.", McpErrorCode.InvalidRequest);
        }
    }

    private static void EnsureFileExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("File does not exist.", path);
        }
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Directory does not exist: {path}");
        }
    }

    private static Encoding ResolveEncoding(string? encoding)
    {
        if (string.IsNullOrWhiteSpace(encoding))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(encoding);
        }
        catch (ArgumentException ex)
        {
            throw new McpProtocolException($"Encoding '{encoding}' is not supported.", ex, McpErrorCode.InvalidParams);
        }
    }

    private static Exception ToMcpException(Exception exception, string resolvedPath)
    {
        return exception switch
        {
            McpProtocolException => exception,
            FileNotFoundException => new McpProtocolException($"File not found: {resolvedPath}", exception, McpErrorCode.ResourceNotFound),
            DirectoryNotFoundException => new McpProtocolException($"Directory not found: {resolvedPath}", exception, McpErrorCode.ResourceNotFound),
            UnauthorizedAccessException => new McpProtocolException($"Access denied: {resolvedPath}", exception, McpErrorCode.InvalidRequest),
            ArgumentException => new McpProtocolException($"Invalid path: {resolvedPath}", exception, McpErrorCode.InvalidParams),
            IOException => new McpProtocolException($"I/O error for path: {resolvedPath}", exception, McpErrorCode.InternalError),
            _ => new McpProtocolException("Unexpected error while handling file system request.", exception, McpErrorCode.InternalError),
        };
    }
}

public sealed record DirectoryEntryDto(string Path, string Name, string EntryType);

public sealed record FileSystemInfoDto(
    string Path,
    string EntryType,
    long? Size,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastModifiedUtc,
    string Attributes);
