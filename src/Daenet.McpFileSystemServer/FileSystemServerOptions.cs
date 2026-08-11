namespace Daenet.McpFileSystemServer;

public sealed class FileSystemServerOptions
{
    public const string RootDirectoryEnvironmentVariable = "MCP_FS_ROOT";
    public const string ReadOnlyEnvironmentVariable = "MCP_FS_READ_ONLY";

    public string RootDirectory { get; init; } = Directory.GetCurrentDirectory();

    public bool ReadOnlyMode { get; init; }

    public static FileSystemServerOptions FromEnvironment()
    {
        string root = Environment.GetEnvironmentVariable(RootDirectoryEnvironmentVariable) ?? Directory.GetCurrentDirectory();
        string readOnlyRaw = Environment.GetEnvironmentVariable(ReadOnlyEnvironmentVariable) ?? "false";

        bool readOnly = bool.TryParse(readOnlyRaw, out bool parsedValue) && parsedValue;

        return new FileSystemServerOptions
        {
            RootDirectory = root,
            ReadOnlyMode = readOnly,
        };
    }
}
