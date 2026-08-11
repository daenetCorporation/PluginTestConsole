using Daenet.McpFileSystemServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

FileSystemServerOptions options = FileSystemServerOptions.FromEnvironment();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<PathSandbox>();
builder.Services.AddSingleton<FileSystemTools>();

builder.Services.AddLogging(logging =>
{
    logging.AddSimpleConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<FileSystemTools>();

using var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Daenet.McpFileSystemServer.Host");
logger.LogInformation("Starting MCP File System Server with root '{RootDirectory}' (ReadOnly={ReadOnlyMode}).", options.RootDirectory, options.ReadOnlyMode);

await host.RunAsync().ConfigureAwait(false);
