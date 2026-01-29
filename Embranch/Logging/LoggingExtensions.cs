using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Embranch.Logging;

/// <summary>
/// Extension methods for configuring file-based logging
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Adds file logging to the logging builder
    /// </summary>
    /// <param name="builder">The logging builder to configure</param>
    /// <param name="logFileName">Optional log file name (defaults to Embranch_[timestamp].log)</param>
    /// <param name="minimumLevel">Minimum log level (defaults to Information)</param>
    /// <returns>The logging builder for chaining</returns>
    public static ILoggingBuilder AddFileLogging(this ILoggingBuilder builder, string? logFileName = null, LogLevel minimumLevel = LogLevel.Information)
    {
        var logPath = GetLogFilePath(logFileName);
        builder.AddProvider(new FileLoggerProvider(logPath, minimumLevel));
        
        // Write startup message
        WriteStartupMessage(logPath);
        
        return builder;
    }

    /// <summary>
    /// PP13-87-C2: Gets the full path for the log file.
    /// - If no logFileName provided: creates timestamped file in exe directory
    /// - If absolute path provided: uses the path directly
    /// - If relative path provided: resolves from current working directory
    /// </summary>
    /// <param name="logFileName">Optional log file name or path from configuration</param>
    /// <returns>Fully resolved path for the log file</returns>
    internal static string GetLogFilePath(string? logFileName)
    {
        // Default: use exe directory with timestamped filename
        if (string.IsNullOrWhiteSpace(logFileName))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            logFileName = $"Embranch_{timestamp}.log";

            var exeLocation = Assembly.GetExecutingAssembly().Location;
            var exeDirectory = Path.GetDirectoryName(exeLocation) ?? Directory.GetCurrentDirectory();

            return Path.Combine(exeDirectory, logFileName);
        }

        // User provided a path - resolve it appropriately
        string resolvedPath;

        if (Path.IsPathRooted(logFileName))
        {
            // Absolute path: use as-is
            resolvedPath = logFileName;
        }
        else
        {
            // Relative path: resolve from current working directory
            resolvedPath = Path.GetFullPath(logFileName);
        }

        // Ensure the directory exists
        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch
            {
                // Fall back to exe directory if we can't create the target directory
                var exeLocation = Assembly.GetExecutingAssembly().Location;
                var exeDirectory = Path.GetDirectoryName(exeLocation) ?? Directory.GetCurrentDirectory();
                var fileName = Path.GetFileName(resolvedPath);
                resolvedPath = Path.Combine(exeDirectory, fileName);
            }
        }

        return resolvedPath;
    }

    /// <summary>
    /// Writes initial startup information to the log file
    /// </summary>
    private static void WriteStartupMessage(string logPath)
    {
        try
        {
            var startupMessage = $@"
================================================================================
Embranch Log
Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Process ID: {Environment.ProcessId}
Machine: {Environment.MachineName}
OS: {Environment.OSVersion}
.NET Version: {Environment.Version}
Log File: {logPath}
================================================================================
";
            File.AppendAllText(logPath, startupMessage);
        }
        catch
        {
            // Ignore startup message write failures
        }
    }
}